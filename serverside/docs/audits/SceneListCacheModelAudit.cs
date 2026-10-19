using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

// Research-only differential model. Not a plugin and not linked into TML.
// Implements the inspected collection/filter contracts, not Unity/FGN/PUP runtime.
internal static class SceneListCacheModelAudit
{
    private sealed class Item : IEquatable<Item>
    {
        internal long Id, Owner;
        internal int Prefab = 1, PositionSector, StorageSector, State, Type;
        internal bool Valid = true, Distant, Created, Baked;
        public override int GetHashCode() { return Id.GetHashCode(); }
        public bool Equals(Item other) { return other != null && Id == other.Id; }
    }
    private sealed class Identity : IEqualityComparer<Item>
    {
        internal static readonly Identity Instance = new Identity();
        public bool Equals(Item x, Item y) { return ReferenceEquals(x, y); }
        public int GetHashCode(Item value) { return RuntimeHelpers.GetHashCode(value); }
    }
    private sealed class Input
    {
        internal int[] Near, Far;
        internal int Mode;
        internal Input(int[] near, int[] far, int mode = 0) { Near = near; Far = far; Mode = mode; }
    }
    private sealed class Counts
    {
        internal long Copies, Filters, Prefabs, Zones;
    }
    private sealed class Lists
    {
        internal readonly List<Item> Near = new List<Item>(), Far = new List<Item>();
    }
    private sealed class World
    {
        internal readonly Dictionary<int, List<Item>> Sectors = new Dictionary<int, List<Item>>();
        internal readonly Dictionary<int, List<Item>> Portals = new Dictionary<int, List<Item>>();
        internal readonly HashSet<int> Loaded = new HashSet<int>(), Prefabs = new HashSet<int>();
        internal Action<int, Item> Changed;
        internal Action DuringCollect;
        internal long NextId = 1;
        internal World() { Prefabs.Add(1); }
        internal Item Add(int sector, bool portal = false)
        {
            Item item = new Item { Id = NextId++, PositionSector = sector, StorageSector = sector };
            AddExisting(item, sector, portal); return item;
        }
        internal void AddExisting(Item item, int sector, bool portal = false)
        {
            Dictionary<int, List<Item>> source = portal ? Portals : Sectors;
            List<Item> bucket;
            if (!source.TryGetValue(sector, out bucket)) source[sector] = bucket = new List<Item>();
            bucket.Add(item); item.StorageSector = sector; Notify(sector, item);
        }
        internal void Remove(Item item, bool portal = false)
        {
            List<Item> bucket;
            if ((portal ? Portals : Sectors).TryGetValue(item.StorageSector, out bucket)) bucket.Remove(item);
            Notify(item.StorageSector, item);
        }
        internal void Move(Item item, int sector)
        {
            // Native SetSector/AddToSector occurs BEFORE m_position is assigned.
            Remove(item); AddExisting(item, sector); item.PositionSector = sector;
        }
        internal void Park(Item item)
        {
            Remove(item); AddExisting(item, 0); item.PositionSector = 1000000; item.Baked = true;
        }
        internal void Metadata(Item item, int prefab, bool distant)
        {
            if (prefab == item.Prefab && distant == item.Distant) return;
            item.Prefab = prefab; item.Distant = distant; Notify(item.StorageSector, item);
        }
        internal void Notify(int sector, Item item) { if (Changed != null) Changed(sector, item); }
        internal Lists Collect(Input input, Counts counts)
        {
            Lists raw = new Lists(); HashSet<int> visited = new HashSet<int>();
            foreach (int sector in input.Near)
            {
                if (!visited.Add(sector)) continue;
                List<Item> bucket;
                if (Sectors.TryGetValue(sector, out bucket)) { raw.Near.AddRange(bucket); counts.Copies += bucket.Count; }
                if (Portals.TryGetValue(sector, out bucket)) { raw.Near.AddRange(bucket); counts.Copies += bucket.Count; }
            }
            foreach (int sector in input.Far)
            {
                if (visited.Contains(sector)) continue;
                List<Item> bucket;
                if (!Sectors.TryGetValue(sector, out bucket)) continue;
                foreach (Item item in bucket) if (item.Distant) { raw.Far.Add(item); counts.Copies++; }
                visited.Add(sector);
            }
            if (DuringCollect != null) { Action callback = DuringCollect; DuringCollect = null; callback(); }
            return raw;
        }
        internal Lists Filter(Lists raw, Counts counts)
        {
            Lists result = new Lists();
            FilterPart(raw.Near, result.Near, true, counts); FilterPart(raw.Far, result.Far, false, counts);
            return result;
        }
        private void FilterPart(List<Item> raw, List<Item> target, bool near, Counts counts)
        {
            foreach (Item item in raw)
            {
                counts.Filters++;
                if (item == null || !item.Valid || item.Id == 0 || item.Prefab == 0 || !Prefabs.Contains(item.Prefab)) continue;
                if (near && !Loaded.Contains(item.PositionSector)) continue;
                target.Add(item);
            }
        }
        internal Lists Baseline(Input input) { Counts counts = new Counts(); return Filter(Collect(input, counts), counts); }
    }
    private sealed class Cache
    {
        private static readonly FieldInfo version = typeof(List<Item>).GetField("_version", BindingFlags.Instance | BindingFlags.NonPublic);
        private World world;
        private int[] near, far;
        private int mode, nearVersion, farVersion;
        private long epoch, builtAt;
        private bool ready;
        private Lists lists;
        private readonly HashSet<int> active = new HashSet<int>();
        private readonly HashSet<Item> references = new HashSet<Item>(Identity.Instance);
        private readonly Dictionary<int, bool> zones = new Dictionary<int, bool>(), prefabs = new Dictionary<int, bool>();
        internal readonly Counts Counts = new Counts();
        internal bool FilterOnly;
        internal int Builds, Hits;
        internal int Retained { get { return references.Count; } }
        internal Lists Get(World next, Input input)
        {
            if (!ReferenceEquals(world, next)) { Clear(); world = next; world.Changed = Changed; }
            bool sameGeometry = near != null && mode == input.Mode && near.SequenceEqual(input.Near) && far.SequenceEqual(input.Far);
            if (!sameGeometry)
            {
                ready = false; near = (int[])input.Near.Clone(); far = (int[])input.Far.Clone(); mode = input.Mode;
                active.Clear(); active.UnionWith(near); active.UnionWith(far);
            }
            long started = epoch;
            // Conservative seam: keep the dependency's original collection on every pass.
            Lists raw = FilterOnly ? world.Collect(input, Counts) : null;
            if (ready && builtAt == epoch && Version(lists.Near) == nearVersion && Version(lists.Far) == farVersion && EnvironmentUnchanged())
            { Hits++; return lists; }
            if (raw == null) raw = world.Collect(input, Counts);
            lists = world.Filter(raw, Counts);
            references.Clear(); zones.Clear(); prefabs.Clear();
            foreach (Item item in raw.Near)
            {
                if (item == null) continue;
                references.Add(item); zones[item.PositionSector] = world.Loaded.Contains(item.PositionSector);
                prefabs[item.Prefab] = world.Prefabs.Contains(item.Prefab);
            }
            foreach (Item item in raw.Far)
            {
                if (item == null) continue;
                references.Add(item); prefabs[item.Prefab] = world.Prefabs.Contains(item.Prefab);
            }
            nearVersion = Version(lists.Near); farVersion = Version(lists.Far);
            builtAt = started; ready = true; Builds++; return lists;
        }
        private bool EnvironmentUnchanged()
        {
            foreach (KeyValuePair<int, bool> entry in zones)
            { Counts.Zones++; if (world.Loaded.Contains(entry.Key) != entry.Value) return false; }
            foreach (KeyValuePair<int, bool> entry in prefabs)
            { Counts.Prefabs++; if (world.Prefabs.Contains(entry.Key) != entry.Value) return false; }
            return true;
        }
        private void Changed(int sector, Item item)
        { if (active.Contains(sector) || (item != null && references.Contains(item))) epoch++; }
        internal void Clear()
        {
            if (world != null) world.Changed = null;
            world = null; lists = null; near = far = null; ready = false;
            active.Clear(); references.Clear(); zones.Clear(); prefabs.Clear(); epoch++;
        }
        // Reflection is acceptable in this isolated model; not a production hot-path implementation.
        private static int Version(List<Item> items) { return (int)version.GetValue(items); }
    }

    private static int checks;
    private static void Check(string name, bool value) { checks++; if (!value) throw new Exception(name); }
    private static bool Same(List<Item> a, List<Item> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (!ReferenceEquals(a[i], b[i]) || a[i].Id != b[i].Id) return false;
        return true;
    }
    private static Lists Verify(string name, World world, Cache cache, Input input)
    {
        Lists expected = world.Baseline(input), actual = cache.Get(world, input);
        Check(name + " near", Same(expected.Near, actual.Near)); Check(name + " far", Same(expected.Far, actual.Far)); return actual;
    }
    private static World Setup()
    {
        World world = new World(); world.Loaded.UnionWith(new[] { 1, 2, 3, 4 }); return world;
    }
    private static void Directed()
    {
        Input input = new Input(new[] { 1, 2, 1 }, new[] { 2, 3 });
        World world = Setup(); Cache cache = new Cache();
        Item one = world.Add(1), two = world.Add(2), far = world.Add(3), portal = world.Add(1, true);
        far.Distant = true;
        Verify("initial overlap precedence and portal", world, cache, input);
        Check("shared sector near not far", cache.Get(world, input).Far.Count == 1);
        int builds = cache.Builds; one.Owner = 50; one.State++; one.Type++; one.Baked = true; one.Created = true;
        Verify("gameplay/owner/type/Created/baked live references", world, cache, input);
        Check("ordinary state does not rebuild", cache.Builds == builds);
        Check("current owner visible", cache.Get(world, input).Near[0].Owner == 50);
        one.Created = false;
        Check("PUP suppression release visible without list rebuild", !cache.Get(world, input).Near[0].Created && cache.Builds == builds);
        Item added = world.Add(1); Verify("spawn", world, cache, input);
        world.Remove(added); added.Valid = false; Verify("destroy/reset", world, cache, input);
        world.Park(one); Verify("PUP park into shared out-of-bounds sector zero", world, cache, input);
        Check("parked reference evicted", !cache.Get(world, input).Near.Contains(one));
        world.Move(one, 1); one.Baked = false; one.Created = false; Verify("PUP restore/repark cycles", world, cache, input);
        Item incoming = world.Add(777); cache.Get(world, input);
        bool sawOldPosition = false; Action<int, Item> observer = world.Changed;
        world.Changed = delegate(int sector, Item item) {
            if (ReferenceEquals(item, incoming) && sector == 1 && item.PositionSector == 777) sawOldPosition = true;
            observer(sector, item);
        };
        world.Move(incoming, 1); world.Changed = observer;
        Check("native-style add payload precedes position assignment", sawOldPosition);
        Verify("enter from uncached sector uses event argument", world, cache, input);
        world.Move(two, 3); two.Distant = false; Verify("native add event sees old position", world, cache, input);
        world.Metadata(two, 1, true); Verify("far membership Distant changed", world, cache, input);
        world.Metadata(one, 99, false); Verify("missing prefab", world, cache, input);
        world.Prefabs.Add(99); Verify("late direct prefab registration without notification", world, cache, input);
        world.Prefabs.Remove(1); world.Prefabs.Add(77); Verify("equal-count prefab registry replacement", world, cache, input);
        world.Prefabs.Add(1); Verify("registry recovered", world, cache, input);
        world.Loaded.Remove(1); world.Loaded.Add(90); Verify("equal-count loaded-zone replacement", world, cache, input);
        world.Loaded.Add(1); Verify("zone reloaded", world, cache, input);
        portal.PositionSector = 4; world.Notify(portal.StorageSector, portal); Verify("portal position without sector transfer", world, cache, input);
        world.Loaded.Remove(4); Verify("portal storage zone differs from filter zone", world, cache, input);
        Item invalid = world.Add(1); invalid.Prefab = 0; Verify("incomplete imported metadata", world, cache, input);
        world.Metadata(invalid, 1, false); Verify("Deserialize completes prefab in same sector", world, cache, input);
        Item reset = world.Add(1); cache.Get(world, input);
        reset.Id = 0; reset.Valid = false; reset.Prefab = -1;
        world.Notify(777, reset);
        Verify("reset notification still finds cached reference after UID/hash change", world, cache, input);
        Item reused = cache.Get(world, input).Near[0]; world.Remove(reused); reused.Id = 99999; reused.PositionSector = 42;
        world.AddExisting(reused, 42); Verify("pooled reference reused elsewhere", world, cache, input);
        builds = cache.Builds; world.Add(72); Verify("unrelated region", world, cache, input);
        Check("unrelated structural activity does not rebuild", cache.Builds == builds);
        Lists exposed = cache.Get(world, input); Item replacement = world.Add(60);
        if (exposed.Near.Count > 0) exposed.Near[0] = replacement;
        Verify("external same-count list mutation", world, cache, input);
        cache.Get(world, input); world.DuringCollect = delegate { world.Notify(1, null); }; world.Notify(1, null);
        cache.Get(world, input); builds = cache.Builds; Verify("invalidation during build is not swallowed", world, cache, input);
        Check("dirty during build forces next rebuild", cache.Builds == builds + 1);
        cache.Get(world, input); builds = cache.Builds; world.Add(1);
        Verify("Awake/destruction callback after collection", world, cache, input);
        Check("consumer activity survives until next pass", cache.Builds == builds + 1);
        Verify("heading/coverage geometry change", world, cache, new Input(new[] { 3, 1 }, new[] { 2 }, 1));
        Verify("peer departure empty coverage", world, cache, new Input(new int[0], new int[0]));
        Check("empty coverage releases objects", cache.Retained == 0);
        World next = Setup(); next.Add(1); Verify("new world same coordinates", next, cache, input);
        cache.Clear(); Check("explicit unload releases references", cache.Retained == 0);
        // A 'nothing was created' result cannot cache a pending-creation queue.
        World pending = Setup(); Item uncreated = pending.Add(1); Cache pendingCache = new Cache();
        pendingCache.Get(pending, input); bool typeReady = false;
        Check("unready object still in candidates", pendingCache.Get(pending, input).Near.Contains(uncreated) && !typeReady);
        builds = pendingCache.Builds; typeReady = true;
        foreach (Item item in pendingCache.Get(pending, input).Near) if (!item.Created && typeReady) item.Created = true;
        Check("existing creation service wakes on readiness without cache invalidation", uncreated.Created && pendingCache.Builds == builds);
        Item[] incorrectlyCachedPending = pendingCache.Get(pending, input).Near.Where(i => !i.Created).ToArray();
        uncreated.Created = false;
        Check("caching only not-Created objects loses a later PUP release", incorrectlyCachedPending.Length == 0 && pendingCache.Get(pending, input).Near.Any(i => !i.Created));
        uncreated.Baked = true;
        Check("baked marker alone cannot exclude native scene candidates", pending.Baseline(input).Near.Contains(uncreated));
        pendingCache.Clear();
    }
    private static void Randomized()
    {
        for (int seed = 0; seed < 8; seed++)
        {
            Random random = new Random(seed); World world = Setup(); Cache cache = new Cache { FilterOnly = seed % 2 == 0 }; List<Item> all = new List<Item>();
            world.Prefabs.UnionWith(Enumerable.Range(1, 40)); world.Loaded.UnionWith(Enumerable.Range(1, 12));
            for (int n = 0; n < 10000; n++)
            {
                Item item = world.Add(1 + n % 12); item.Prefab = 1 + n % 40; item.Distant = n % 3 == 0; all.Add(item);
            }
            Input input = new Input(Enumerable.Range(1, 9).ToArray(), Enumerable.Range(7, 6).ToArray());
            for (int step = 0; step < 250; step++)
            {
                Item item = all[random.Next(all.Count)];
                switch (random.Next(9))
                {
                    case 0: item.Owner++; item.State++; item.Created = !item.Created; item.Baked = !item.Baked; break;
                    case 1: world.Move(item, random.Next(1, 20)); break;
                    case 2: world.Metadata(item, random.Next(0, 45), !item.Distant); break;
                    case 3: if (world.Loaded.Contains(item.PositionSector)) world.Loaded.Remove(item.PositionSector); else world.Loaded.Add(item.PositionSector); break;
                    case 4: if (world.Prefabs.Contains(item.Prefab)) world.Prefabs.Remove(item.Prefab); else world.Prefabs.Add(item.Prefab); break;
                    case 5: world.Remove(item); item.Id = world.NextId++; item.PositionSector = random.Next(1, 20); world.AddExisting(item, item.PositionSector); break;
                    case 6: all.Add(world.Add(random.Next(1, 20))); break;
                    case 7: item.Valid = !item.Valid; world.Notify(item.StorageSector, item); break;
                    case 8: input = new Input(Enumerable.Range(random.Next(1, 5), 9).ToArray(), Enumerable.Range(10, 5).ToArray()); break;
                }
                Verify("random seed " + seed + " step " + step, world, cache, input);
            }
            cache.Clear(); Check("random teardown bounded " + seed, cache.Retained == 0);
        }
    }
    private static void StableWork()
    {
        World world = Setup(); world.Prefabs.UnionWith(Enumerable.Range(1, 100)); Cache cache = new Cache();
        for (int i = 0; i < 10000; i++) { Item item = world.Add(1 + i % 4); item.Prefab = 1 + i % 100; }
        Input input = new Input(new[] { 1, 2, 3, 4 }, new int[0]);
        cache.Get(world, input);
        long copied = cache.Counts.Copies, filtered = cache.Counts.Filters;
        for (int tick = 0; tick < 1000; tick++)
        {
            world.Sectors[1][0].Owner++; world.Sectors[2][0].State++;
            world.Add(100000 + tick); // Other regions cannot invalidate the base.
            cache.Get(world, input);
        }
        Check("1000 stable passes do not recollect", cache.Counts.Copies == copied);
        Check("1000 stable passes do not refilter", cache.Counts.Filters == filtered);
        Check("1000 stable passes retain exactly active objects", cache.Retained == 10000);
        Console.WriteLine("MODEL ONLY: 10000 objects; 1000 cache hits; saved 10000000 filter visits and reference copies after cold build; checks=" + cache.Counts.Prefabs + " prefab, " + cache.Counts.Zones + " zone. No timing/performance claim.");
        Item[] parked = world.Sectors.Values.SelectMany(v => v).Where(i => i.PositionSector <= 4).ToArray();
        int builds = cache.Builds;
        foreach (Item item in parked) world.Park(item);
        Verify("10000-item PUP park batch", world, cache, input);
        Check("park batch coalesces to one rebuild", cache.Builds == builds + 1 && cache.Retained == 0);
        builds = cache.Builds;
        for (int i = 0; i < parked.Length; i++) { world.Move(parked[i], 1 + i % 4); parked[i].Baked = false; }
        Verify("10000-item PUP restore batch", world, cache, input);
        Check("restore batch coalesces to one rebuild", cache.Builds == builds + 1 && cache.Retained == 10000);
        cache.Clear();
        Cache filterOnly = new Cache { FilterOnly = true };
        filterOnly.Get(world, input); copied = filterOnly.Counts.Copies; filtered = filterOnly.Counts.Filters;
        for (int tick = 0; tick < 1000; tick++) filterOnly.Get(world, input);
        Check("filter-only mode preserves all collection copies", filterOnly.Counts.Copies - copied == 10000000);
        Check("filter-only mode still avoids repeated filtering", filterOnly.Counts.Filters == filtered);
        Verify("filter-only final differential comparison", world, filterOnly, input);
        Console.WriteLine("MODEL ONLY: filter-only seam preserves collection; 10000 objects, 1000 hits, saves 10000000 filter visits; reference copies retained. No timing/performance claim.");
        filterOnly.Clear();
    }
    private static void BoundaryCountermodels()
    {
        // PUP uses floor(x/64); vanilla uses floor((x+32)/64).
        Check("PUP and vanilla cell boundaries differ", Math.Floor(40.0 / 64) != Math.Floor((40.0 + 32) / 64));
        // A count of loaded zones does not prove identical membership.
        HashSet<int> before = new HashSet<int>(new[] { 1, 2 }), after = new HashSet<int>(new[] { 2, 3 });
        Check("loaded-count shortcut has counterexample", before.Count == after.Count && !before.SetEquals(after));
        Item changing = new Item { Id = 100 };
        HashSet<Item> unsafeKeys = new HashSet<Item> { changing };
        HashSet<Item> referenceKeys = new HashSet<Item>(Identity.Instance) { changing };
        changing.Id = 200;
        Check("mutable ZDO-style hash loses ordinary-set lookup", !unsafeKeys.Contains(changing) && referenceKeys.Contains(changing));
    }
    private static int Main()
    {
        try { Directed(); Randomized(); StableWork(); BoundaryCountermodels(); Console.WriteLine("Scene candidate cache MODEL: " + checks + " assertions passed; no Unity/network/production integration executed."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
