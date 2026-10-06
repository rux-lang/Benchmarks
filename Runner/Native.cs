using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Runner;

/// <summary>
/// Operating-system calls for exact per-process measurements. .NET's Process class cannot report
/// CPU time or peak memory once a process has exited, so the runner asks the OS directly.
/// </summary>
static partial class Native
{
    // ---------------------------------------------------------------- Windows

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessMemoryCounters
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);

    [LibraryImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessMemoryInfo(SafeProcessHandle process, out ProcessMemoryCounters counters, uint size);

    /// <summary>
    /// Starts the process, waits for it, then reads its times and peak working set from the
    /// handle, which stays valid until the Process object is disposed.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static RunSample MeasureWindows(ProcessStartInfo info, int timeoutSeconds)
    {
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"cannot start {info.FileName}");
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutSeconds * 1000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{info.FileName} did not finish in {timeoutSeconds} s");
        }
        process.WaitForExit();

        if (!GetProcessTimes(process.SafeHandle, out long creation, out long exit, out long kernel, out long user))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var counters = new ProcessMemoryCounters { Size = (uint)Marshal.SizeOf<ProcessMemoryCounters>() };
        if (!GetProcessMemoryInfo(process.SafeHandle, out counters, counters.Size))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        // FILETIME values count 100-nanosecond ticks.
        return new RunSample(process.ExitCode, stdout.Result, stderr.Result,
            WallMs: (exit - creation) / 10_000.0,
            UserMs: user / 10_000.0,
            SysMs: kernel / 10_000.0,
            PeakBytes: (long)counters.PeakWorkingSetSize);
    }

    // ---------------------------------------------------------------- Linux

    [StructLayout(LayoutKind.Sequential)]
    struct ResourceUsage
    {
        public long UserSeconds, UserMicroseconds;
        public long SystemSeconds, SystemMicroseconds;
        public long MaxResidentKilobytes;
        public long IxRss, IdRss, IsRss, MinorFaults, MajorFaults, Swaps, BlockIn, BlockOut;
        public long MessagesSent, MessagesReceived, Signals, VoluntarySwitches, InvoluntarySwitches;
    }

    const int OpenReadOnly = 0, OpenWriteOnly = 1, OpenCreate = 0x40, OpenTruncate = 0x200;
    const int EINTR = 4, SIGKILL = 9;

    [LibraryImport("libc", SetLastError = true)]
    private static partial int posix_spawn_file_actions_init(nint actions);
    [LibraryImport("libc", SetLastError = true)]
    private static partial int posix_spawn_file_actions_destroy(nint actions);
    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int posix_spawn_file_actions_addopen(nint actions, int fd, string path, int flags, int mode);
    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int posix_spawn_file_actions_addchdir_np(nint actions, string path);
    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int posix_spawn(out int pid, string path, nint actions, nint attributes, nint[] argv, nint[] envp);
    [LibraryImport("libc", SetLastError = true)]
    private static partial int wait4(int pid, out int status, int options, out ResourceUsage usage);
    [LibraryImport("libc", SetLastError = true)]
    private static partial int kill(int pid, int signal);

    static nint[] ToCStrings(IEnumerable<string> values)
    {
        var list = values.Select(v => Marshal.StringToCoTaskMemUTF8(v)).ToList();
        list.Add(0);
        return [.. list];
    }

    static void FreeCStrings(nint[] values)
    {
        foreach (nint value in values)
            if (value != 0)
                Marshal.FreeCoTaskMem(value);
    }

    /// <summary>
    /// Starts the program with posix_spawn and collects it with wait4, which returns the child's
    /// CPU times and peak resident set size. .NET's Process class reaps its own children only, so
    /// it never collects this one.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public static RunSample MeasureLinux(string executable, IReadOnlyList<string> arguments, string workingDirectory,
        IReadOnlyDictionary<string, string> environment, int timeoutSeconds)
    {
        string stdoutPath = Path.GetTempFileName();
        string stderrPath = Path.GetTempFileName();
        nint actions = Marshal.AllocHGlobal(256);
        nint[] argv = ToCStrings([executable, .. arguments]);
        nint[] envp = ToCStrings(environment.Select(e => $"{e.Key}={e.Value}"));
        try
        {
            Check(posix_spawn_file_actions_init(actions), "posix_spawn_file_actions_init");
            Check(posix_spawn_file_actions_addopen(actions, 0, "/dev/null", OpenReadOnly, 0), "addopen stdin");
            Check(posix_spawn_file_actions_addopen(actions, 1, stdoutPath, OpenWriteOnly | OpenCreate | OpenTruncate, 0x180), "addopen stdout");
            Check(posix_spawn_file_actions_addopen(actions, 2, stderrPath, OpenWriteOnly | OpenCreate | OpenTruncate, 0x180), "addopen stderr");
            Check(posix_spawn_file_actions_addchdir_np(actions, workingDirectory), "addchdir");

            var stopwatch = Stopwatch.StartNew();
            Check(posix_spawn(out int pid, executable, actions, 0, argv, envp), "posix_spawn");
            var waiter = Task.Run(() =>
            {
                while (true)
                {
                    int result = wait4(pid, out int status, 0, out ResourceUsage usage);
                    if (result == pid)
                        return (status, usage, stopwatch.Elapsed.TotalMilliseconds);
                    if (Marshal.GetLastPInvokeError() != EINTR)
                        throw new Win32Exception(Marshal.GetLastPInvokeError(), "wait4");
                }
            });
            if (!waiter.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
            {
                kill(pid, SIGKILL);
                waiter.Wait();
                throw new TimeoutException($"{executable} did not finish in {timeoutSeconds} s");
            }
            var (rawStatus, rusage, wallMs) = waiter.Result;
            int exitCode = (rawStatus & 0x7f) == 0 ? (rawStatus >> 8) & 0xff : 128 + (rawStatus & 0x7f);
            return new RunSample(exitCode, File.ReadAllText(stdoutPath), File.ReadAllText(stderrPath),
                WallMs: wallMs,
                UserMs: rusage.UserSeconds * 1000.0 + rusage.UserMicroseconds / 1000.0,
                SysMs: rusage.SystemSeconds * 1000.0 + rusage.SystemMicroseconds / 1000.0,
                PeakBytes: rusage.MaxResidentKilobytes * 1024);
        }
        finally
        {
            posix_spawn_file_actions_destroy(actions);
            Marshal.FreeHGlobal(actions);
            FreeCStrings(argv);
            FreeCStrings(envp);
            File.Delete(stdoutPath);
            File.Delete(stderrPath);
        }
    }

    static void Check(int result, string what)
    {
        // posix_spawn functions return the error number instead of setting errno.
        if (result != 0)
            throw new Win32Exception(result, what);
    }
}
