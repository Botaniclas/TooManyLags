using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TooManyLags;

internal static class HandoffSendPriorityTests
{
    private static int checks;
    private sealed class Item { internal int Tier, Id; }
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static int Tier(Item item) { return item.Tier; }
    private static void Empty(HandoffSendPriority<Item> sorter)
    {
        foreach (string field in new[] { "positions", "lanes" })
        {
            object value = typeof(HandoffSendPriority<Item>).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sorter);
            if (field == "positions") Check(((ICollection)value).Count == 0, "position snapshot cleared");
            else foreach (ICollection lane in (IEnumerable)value) Check(lane.Count == 0, "packet snapshot releases ZDO references");
        }
    }
    public static int Main()
    {
        try
        {
            var sorter = new HandoffSendPriority<Item>();
            var items = new List<Item>();
            for (int i = 0; i < 1000; i++) items.Add(new Item { Tier = (i % 5) - 1, Id = i });
            var before = new List<Item>(items); int prefix = 751;
            var expected = new List<Item>();
            for (int tier = 0; tier < 4; tier++) for (int i = 0; i < prefix; i++) if (before[i].Tier == tier) expected.Add(before[i]);
            sorter.Apply(items, prefix, Tier);
            int index = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (i >= prefix || before[i].Tier < 0) Check(ReferenceEquals(items[i], before[i]), "unrelated forced slots and normal suffix retain exact positions");
                else Check(ReferenceEquals(items[i], expected[index++]), "managed forced prefix is stable red/forward/outer/return order");
            }
            Check(items.Count == before.Count, "no package entry added or removed"); Empty(sorter);
            var sorted = new List<Item>(items); sorter.Apply(items, prefix, Tier);
            for (int i = 0; i < items.Count; i++) Check(ReferenceEquals(items[i], sorted[i]), "already ordered packets are unchanged");
            int calls = 0;
            sorter.Apply(items, 1, delegate(Item item) { calls++; return item.Tier; });
            sorter.Apply(items, items.Count + 1, delegate(Item item) { calls++; return item.Tier; });
            Check(calls == 0, "zero/single/invalid inserted range costs no per-item work");
            bool caught = false;
            try { sorter.Apply(items, prefix, delegate(Item item) { throw new InvalidOperationException(); }); }
            catch (InvalidOperationException) { caught = true; }
            Check(caught, "caller retains native exception handling"); Empty(sorter);
            var duplicate = new Item { Tier = 0, Id = 2000 };
            items = new List<Item> { new Item { Tier = 3 }, duplicate, duplicate };
            sorter.Apply(items, 3, Tier);
            Check(ReferenceEquals(items[0], duplicate) && ReferenceEquals(items[1], duplicate) && items[2].Tier == 3,
                "native duplicate entries retain multiplicity");
            sorter.Apply(items, 3, delegate(Item item) { sorter.Apply(items, 3, Tier); return item.Tier; }); Empty(sorter);
            Console.WriteLine("PASS: " + checks + " native forced-prefix priority and retention checks."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
