using System;
using System.Reflection;
internal static class UnloadRegression
{
    public static void Main()
    {
        try
        {
            typeof(IntegrationTests).GetMethod("DestroyedViewCleanup",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            Console.WriteLine("PASS: destroyed Unity wrapper cleanup regression.");
        }
        catch(Exception error){Console.WriteLine("FAILED: "+error.GetBaseException().Message);Environment.ExitCode=1;}
    }
}
