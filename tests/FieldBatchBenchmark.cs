using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

internal interface IFieldRunner { void Step(); long Calls { get; } long Trace { get; } }

internal static class FieldBatchBenchmark
{
    private static double Measure(IFieldRunner runner, int iterations)
    {
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) runner.Step();
        timer.Stop(); return timer.Elapsed.TotalMilliseconds / iterations;
    }
    public static int Main()
    {
        try
        {
            foreach (bool due in new[] { false, true }) foreach (int fields in new[] { 1, 64, 4096 })
            {
                IFieldRunner baseline = new Baseline.Runner(fields, due), candidate = new Candidate.Runner(fields, due);
                for (int i = 0; i < 20; i++) { baseline.Step(); candidate.Step(); }
                var before = new double[9]; var after = new double[9];
                for (int sample = 0; sample < 9; sample++)
                {
                    if ((sample & 1) == 0) { before[sample] = Measure(baseline, 200); after[sample] = Measure(candidate, 200); }
                    else { after[sample] = Measure(candidate, 200); before[sample] = Measure(baseline, 200); }
                }
                if (baseline.Calls != candidate.Calls || baseline.Trace != candidate.Trace)
                    throw new Exception("Apply trace changed");
                Array.Sort(before); Array.Sort(after);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "4096 jobs; fields={0}; due={1}; baseline_ms={2:F6}; candidate_ms={3:F6}; change_pct={4:F1}; identical_calls={5}; trace={6}",
                    fields, due, before[4], after[4], (after[4] / before[4] - 1) * 100, candidate.Calls, candidate.Trace));
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
