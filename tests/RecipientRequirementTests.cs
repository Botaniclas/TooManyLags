using System;
using System.Runtime.CompilerServices;
using TooManyLags.GridHandoffs;

internal static class RecipientRequirementTests
{
    private static int checks;
    private static void Check(string name, bool result) { checks++; if (!result) throw new Exception(name); }
    public static void Main()
    {
        var records = new ConditionalWeakTable<object, RecipientRequirement>();
        object key = new object(); RecipientRequirement record;
        Check("unchanged objects have no readiness requirement", !records.TryGetValue(key, out record));
        record = records.GetOrCreateValue(key); record.Include(7, 4, 100);
        Check("new requirement matches only its owner tuple", record.Matches(7, 4) && !record.Matches(8, 4) && !record.Matches(7, 5));
        record.Include(7, 4, 80); Check("server observation cannot lower source floor", record.State == 100);
        record.Include(7, 4, 120); Check("newer source state raises floor", record.State == 120);
        record.Include(8, 5, 30); Check("different owner tuple starts a new floor", record.Matches(8, 5) && record.State == 30);
        for (int i=0;i<10000;i++) record.Include(8,5,20);
        Check("repeated observations retain maximum without collection growth", record.State == 30 && Object.ReferenceEquals(record, records.GetOrCreateValue(key)));
        records.Remove(key); Check("unload removes requirement", !records.TryGetValue(key, out record));
        record = records.GetOrCreateValue(key); record.Include(8,5,10);
        Check("reload has independent readiness state",record.State==10);
        records = new ConditionalWeakTable<object, RecipientRequirement>();
        Check("reset clears scope", !records.TryGetValue(key, out record));
        Console.WriteLine("PASS: " + checks + " recipient scope/state-floor lifecycle assertions.");
    }
}
