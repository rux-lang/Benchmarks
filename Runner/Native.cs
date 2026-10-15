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

    // ---------------------------------------------------------------- Windows job objects

    [StructLayout(LayoutKind.Sequential)]
    struct ExtendedLimitInformation
    {
        // JOBOBJECT_BASIC_LIMIT_INFORMATION
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
        // IO_COUNTERS
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    const int JobObjectExtendedLimitInformation = 9;
    const uint JobObjectLimitKillOnJobClose = 0x2000;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
    private static partial nint CreateJobObject(nint attributes, nint name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, SafeProcessHandle process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(nint job, int infoClass, in ExtendedLimitInformation info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryInformationJobObject(nint job, int infoClass, out ExtendedLimitInformation info, uint size, nint returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    /// <summary>
    /// A Windows job object holding a build: every process the build tool starts joins it, so the
    /// kernel tracks their memory together. Closing the job ends any process still left in it
    /// (such as a linker's PDB server).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class BuildJob : IDisposable
    {
        readonly nint handle;

        public BuildJob()
        {
            handle = CreateJobObject(0, 0);
            if (handle == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject");
            var limits = new ExtendedLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose };
            if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, limits, (uint)Marshal.SizeOf<ExtendedLimitInformation>()))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject");
        }

        /// <summary>
        /// Adds a process that was just started. Processes it starts from now on join the job
        /// too; a build tool needs far longer to start its first child than this call takes.
        /// </summary>
        public void Add(Process process)
        {
            if (!AssignProcessToJobObject(handle, process.SafeHandle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject");
        }

        /// <summary>The most memory committed by all processes of the job together at any moment.</summary>
        public long PeakBytes
        {
            get
            {
                if (!QueryInformationJobObject(handle, JobObjectExtendedLimitInformation, out var info, (uint)Marshal.SizeOf<ExtendedLimitInformation>(), 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "QueryInformationJobObject");
                return (long)info.PeakJobMemoryUsed;
            }
        }

        public void Dispose() => CloseHandle(handle);
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
    const int EINTR = 4, SIGKILL = 9, CloseOnExec = 0x80000, PR_SET_CHILD_SUBREAPER = 36;

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
    [LibraryImport("libc", SetLastError = true)]
    private static partial int posix_spawn_file_actions_adddup2(nint actions, int fd, int newFd);
    [LibraryImport("libc", SetLastError = true)]
    private static partial int pipe2(int[] fds, int flags);
    [LibraryImport("libc", SetLastError = true)]
    private static partial nint read(int fd, byte[] buffer, nint count);
    [LibraryImport("libc", SetLastError = true)]
    private static partial nint write(int fd, byte[] buffer, nint count);
    [LibraryImport("libc", SetLastError = true)]
    private static partial int close(int fd);
    [LibraryImport("libc", SetLastError = true)]
    private static partial int prctl(int option, nuint arg2, nuint arg3, nuint arg4, nuint arg5);

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
    /// Starts the program and collects it with wait4, which returns its CPU times and peak resident
    /// set size. A process's peak includes the peak of the memory it ran in before its exec, and
    /// posix_spawn (like fork) starts the child in the runner's memory, so a program the runner
    /// started itself would report at least the runner's own peak. A small /bin/sh starts it instead
    /// and exits; the runner, a child subreaper, adopts the program and collects it. The program
    /// waits on a pipe until the runner releases it, and the clock starts then. .NET's Process class
    /// reaps its own children only, so it never collects these.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public static RunSample MeasureLinux(string executable, IReadOnlyList<string> arguments, string workingDirectory,
        IReadOnlyDictionary<string, string> environment, int timeoutSeconds)
    {
        CheckErrno(prctl(PR_SET_CHILD_SUBREAPER, 1, 0, 0, 0), "prctl");
        string stdoutPath = Path.GetTempFileName();
        string stderrPath = Path.GetTempFileName();
        nint actions = Marshal.AllocHGlobal(256);
        // The shell runs the program in the background with "$@", which waits for a line on fd 3, and
        // writes its process id to fd 4. A shell sets PWD (to the working directory), and bash sets
        // SHLVL; the program gets them only when the runner passes them.
        string unset = string.Concat(new[] { "PWD", "SHLVL" }.Where(v => !environment.ContainsKey(v)).Select(v => $"unset {v}; "));
        string script = "{ read -r _ <&3 || exit 1; " + unset + "exec \"$@\" 3<&- 4>&-; } & echo $! >&4";
        nint[] argv = ToCStrings(["sh", "-c", script, "sh", executable, .. arguments]);
        nint[] envp = ToCStrings(environment.Select(e => $"{e.Key}={e.Value}"));
        int[] go = [-1, -1], id = [-1, -1];
        try
        {
            CheckErrno(pipe2(go, CloseOnExec), "pipe2");
            CheckErrno(pipe2(id, CloseOnExec), "pipe2");
            Check(posix_spawn_file_actions_init(actions), "posix_spawn_file_actions_init");
            Check(posix_spawn_file_actions_addopen(actions, 0, "/dev/null", OpenReadOnly, 0), "addopen stdin");
            Check(posix_spawn_file_actions_addopen(actions, 1, stdoutPath, OpenWriteOnly | OpenCreate | OpenTruncate, 0x180), "addopen stdout");
            Check(posix_spawn_file_actions_addopen(actions, 2, stderrPath, OpenWriteOnly | OpenCreate | OpenTruncate, 0x180), "addopen stderr");
            Check(posix_spawn_file_actions_adddup2(actions, go[0], 3), "adddup2 fd 3");
            Check(posix_spawn_file_actions_adddup2(actions, id[1], 4), "adddup2 fd 4");
            Check(posix_spawn_file_actions_addchdir_np(actions, workingDirectory), "addchdir");

            Check(posix_spawn(out int shell, "/bin/sh", actions, 0, argv, envp), "posix_spawn");
            CloseFd(ref go[0]);
            CloseFd(ref id[1]);
            string line = ReadLine(id[0]);
            CloseFd(ref id[0]);
            Wait(shell);
            if (!int.TryParse(line, out int pid))
                throw new InvalidOperationException($"/bin/sh did not start {executable}\n{File.ReadAllText(stderrPath).Trim()}");

            var stopwatch = Stopwatch.StartNew();
            if (write(go[1], "\n"u8.ToArray(), 1) != 1)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "write");
            CloseFd(ref go[1]);
            var waiter = Task.Run(() =>
            {
                var (status, usage) = Wait(pid);
                return (status, usage, stopwatch.Elapsed.TotalMilliseconds);
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
            foreach (int[] pipe in (int[][])[go, id])
                for (int i = 0; i < 2; i++)
                    CloseFd(ref pipe[i]);
            posix_spawn_file_actions_destroy(actions);
            Marshal.FreeHGlobal(actions);
            FreeCStrings(argv);
            FreeCStrings(envp);
            File.Delete(stdoutPath);
            File.Delete(stderrPath);
        }
    }

    /// <summary>Waits for a child (or an adopted orphan) to exit and collects it.</summary>
    static (int Status, ResourceUsage Usage) Wait(int pid)
    {
        while (true)
        {
            if (wait4(pid, out int status, 0, out ResourceUsage usage) == pid)
                return (status, usage);
            if (Marshal.GetLastPInvokeError() != EINTR)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "wait4");
        }
    }

    /// <summary>Reads up to the first newline or the end of the pipe.</summary>
    static string ReadLine(int fd)
    {
        var bytes = new List<byte>();
        var buffer = new byte[1];
        while (true)
        {
            nint count = read(fd, buffer, 1);
            if (count < 0 && Marshal.GetLastPInvokeError() == EINTR)
                continue;
            if (count <= 0 || buffer[0] == '\n')
                return System.Text.Encoding.ASCII.GetString([.. bytes]);
            bytes.Add(buffer[0]);
        }
    }

    static void CloseFd(ref int fd)
    {
        if (fd >= 0)
            close(fd);
        fd = -1;
    }

    static void Check(int result, string what)
    {
        // posix_spawn functions return the error number instead of setting errno.
        if (result != 0)
            throw new Win32Exception(result, what);
    }

    /// <summary>For calls that return -1 and set errno.</summary>
    static void CheckErrno(int result, string what)
    {
        if (result == -1)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), what);
    }
}
