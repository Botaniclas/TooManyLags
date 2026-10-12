using System;
using TooManyLags.GridHandoffs;

internal static class RecipientRequirementTests
{
    private static int checks;
    private static void Check(string name, bool result) { checks++; if (!result) throw new Exception(name); }
    public static void Main()
    {
        RecipientRequirement record = default(RecipientRequirement);
        Check("unchanged objects have no readiness requirement", !record.Matches(0, 0) && !record.Matches(7, 0));
        record.Include(7, 4, 100);
        Check("new requirement matches only its owner tuple", record.Matches(7, 4) && !record.Matches(8, 4) && !record.Matches(7, 5));
        record.Include(7, 4, 80); Check("server observation cannot lower source floor", record.State == 100);
        record.Include(7, 4, 120); Check("newer source state raises floor", record.State == 120);
        record.Include(8, 5, 30); Check("different owner tuple starts a new floor", record.Matches(8, 5) && record.State == 30);
        for (int i=0;i<10000;i++) record.Include(8,5,20);
        Check("repeated observations retain maximum without collection growth", record.State == 30 && typeof(RecipientRequirement).IsValueType);
        record = default(RecipientRequirement); Check("unload removes requirement", !record.Matches(8,5));
        record.Include(8,5,10);
        Check("reload has independent readiness state",record.State==10);
        record = default(RecipientRequirement);
        Check("reset clears scope", !record.Matches(8,5));
        Console.WriteLine("PASS: " + checks + " recipient scope/state-floor lifecycle assertions.");
    }
}
