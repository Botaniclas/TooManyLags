using System;
using System.IO;
using System.Runtime.InteropServices;

// Isolated host for managed fixtures using the game's embedded Mono and BCL.
// Never starts Unity, a world or network transport; game installation read-only.
internal static class StateMonoFixtureHost
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string path);
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void mono_set_dirs(string assemblyDirectory, string configDirectory);
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void mono_set_assemblies_path(string path);
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mono_jit_init_version(string name, string version);
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mono_domain_assembly_open(IntPtr domain, string path);
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int mono_jit_exec(IntPtr domain, IntPtr assembly, int argc,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] argv);
    public static void Main(string[] args)
    {
        try
        {
            string fixture = Path.GetFullPath(args[0]);
            if (!SetDllDirectory(args[2])) throw new Exception("Cannot select Mono DLL directory.");
            mono_set_dirs(args[1], args[3]);
            mono_set_assemblies_path(Path.GetDirectoryName(fixture) + ";" + args[1]);
            IntPtr domain = mono_jit_init_version("TooManyLags managed fixture", "v4.0.30319");
            if (domain == IntPtr.Zero) throw new Exception("Mono initialization failed.");
            IntPtr assembly = mono_domain_assembly_open(domain, fixture);
            if (assembly == IntPtr.Zero) throw new Exception("Mono fixture assembly load failed.");
            Environment.ExitCode = mono_jit_exec(domain, assembly, 5, new[] { fixture, args[4], args[1], args[5], args[6] });
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
}

