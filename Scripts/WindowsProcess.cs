// Windows launch and measurement plumbing only. No benchmark calculations live here.
// The child starts suspended so affinity and lifetime control apply before its first instruction.
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class WindowsProcess
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Startup {
        public int cb; public string reserved, desktop, title;
        public int x,y,xSize,ySize,xCount,yCount,fill,flags;
        public short show,reserved2; public IntPtr reservedPtr,input,output,error;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInfo { public IntPtr process, thread; public uint pid, tid; }
    [StructLayout(LayoutKind.Sequential)]
    struct Counters {
        public uint cb, faults;
        public UIntPtr peakWorkingSet, workingSet, peakPaged, paged, peakNonpaged, nonpaged, pagefile, peakPagefile;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct BasicLimits {
        public long perProcessTime, perJobTime; public uint flags;
        public UIntPtr minWorking, maxWorking; public uint active;
        public UIntPtr affinity; public uint priority, scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct IoCounters { public ulong a,b,c,d,e,f; }
    [StructLayout(LayoutKind.Sequential)]
    struct ExtendedLimits {
        public BasicLimits basic; public IoCounters io;
        public UIntPtr processMemory, jobMemory, peakProcessMemory, peakJobMemory;
    }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern bool CreateProcessW(string app, StringBuilder command, IntPtr pa, IntPtr ta,
        bool inherit, uint flags, IntPtr environment, string directory, ref Startup startup, out ProcessInfo pi);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool SetHandleInformation(IntPtr h, uint mask, uint flags);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool SetProcessAffinityMask(IntPtr h, UIntPtr mask);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetProcessAffinityMask(IntPtr h, out UIntPtr mask, out UIntPtr system);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern uint ResumeThread(IntPtr h);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h, out uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr CreateJobObjectW(IntPtr a, string name);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool SetInformationJobObject(IntPtr h, int info, ref ExtendedLimits limits, uint length);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr h, uint code);
    [DllImport("psapi.dll", SetLastError=true)] static extern bool GetProcessMemoryInfo(IntPtr h, out Counters c, uint size);
    public sealed class Result {
        public uint ExitCode;
        public ulong PeakWorkingSetBytes;
        public bool TimedOut;
    }
    public static ulong AllowedMask() {
        if (!GetProcessAffinityMask(GetCurrentProcess(), out var mask, out _)) throw new Win32Exception();
        return mask.ToUInt64();
    }
    static void Inherit(FileStream f) {
        if (!SetHandleInformation(f.SafeFileHandle.DangerousGetHandle(), 1, 1)) throw new Win32Exception();
    }
    public static Result Run(string command, string directory, string input, string output, string error, int cpu, int timeoutSeconds)
    {
        using var stdin = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var stdout = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var stderr = new FileStream(error, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        Inherit(stdin); Inherit(stdout); Inherit(stderr);
        var startup = new Startup { cb=Marshal.SizeOf<Startup>(), flags=0x100,
            input=stdin.SafeFileHandle.DangerousGetHandle(), output=stdout.SafeFileHandle.DangerousGetHandle(),
            error=stderr.SafeFileHandle.DangerousGetHandle() };
        var job = CreateJobObjectW(IntPtr.Zero, null);
        var pi = new ProcessInfo();
        try {
            if (job == IntPtr.Zero) throw new Win32Exception();
            var limits = new ExtendedLimits();
            limits.basic.flags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>())) throw new Win32Exception();
            if (!CreateProcessW(null, new StringBuilder(command), IntPtr.Zero, IntPtr.Zero, true,
                0x08000004, IntPtr.Zero, directory, ref startup, out pi)) throw new Win32Exception();
            if (!AssignProcessToJobObject(job, pi.process)) throw new Win32Exception();
            if (cpu >= 0 && !SetProcessAffinityMask(pi.process, (UIntPtr)(1UL << cpu))) throw new Win32Exception();
            if (ResumeThread(pi.thread) == uint.MaxValue) throw new Win32Exception();
            uint wait = WaitForSingleObject(pi.process, checked((uint)timeoutSeconds * 1000));
            if (wait == 0x102) return new Result { ExitCode=124, TimedOut=true };
            if (wait != 0) throw new Win32Exception();
            if (!GetExitCodeProcess(pi.process, out var exit)) throw new Win32Exception();
            if (!GetProcessMemoryInfo(pi.process, out var memory, (uint)Marshal.SizeOf<Counters>())) throw new Win32Exception();
            return new Result { ExitCode=exit, PeakWorkingSetBytes=memory.peakWorkingSet.ToUInt64() };
        }
        finally {
            // Also handles interruption, setup failure and a suspended child that never resumed.
            if (pi.process != IntPtr.Zero) { TerminateProcess(pi.process, 125); CloseHandle(pi.process); }
            if (pi.thread != IntPtr.Zero) CloseHandle(pi.thread);
            if (job != IntPtr.Zero) CloseHandle(job);
        }
    }
}

