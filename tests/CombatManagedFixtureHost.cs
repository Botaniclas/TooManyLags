using System;
using System.Reflection;

// Generic isolated CLR host. Fixtures stay in a managed assembly and never
// start Unity, a game world or network transport.
internal static class ManagedFixtureHost
{
    public static int Main(string[] args)
    {
        try
        {
            MethodInfo entry = Assembly.LoadFrom(args[0]).EntryPoint;
            if (entry == null) throw new MissingMethodException("Fixture entry point missing.");
            object result = entry.Invoke(null, entry.GetParameters().Length == 0 ? null : new object[] { new string[0] });
            return result is int ? (int)result : Environment.ExitCode;
        }
        catch (Exception error) { Console.Error.WriteLine(error.GetBaseException()); return 1; }
    }
}
