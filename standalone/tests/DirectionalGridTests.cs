using System;
using System.Collections.Generic;
using TooManyLagsStandalone;

internal static class DirectionalGridTests
{
    private static int checks;
    private static void Check(string label, bool value) { checks++; if (!value) throw new Exception(label); }
    private static List<GridPeer> Peers(params GridPeer[] peers) { return new List<GridPeer>(peers); }
    private static GridPeer P(long uid, int x = 0, int z = 0, float yaw = 0) { return new GridPeer(uid, new Cell(x, z), yaw); }
    // Independent row tables of the two drawn/cardinal and diagonal masks.
    // No production dot-product or coverage-counter algorithm is used here.
    private static int Region(int x, int z, int heading, int radius)
    {
        int[] widths = radius == 3 ? new[] { 3, 3, 2, 1 } : radius == 2 ? new[] { 2, 2, 1 } : new[] { 1, 1 };
        if (Math.Abs(z) > radius || Math.Abs(x) > widths[Math.Abs(z)]) return 0;
        for (int turn = 0; turn < heading / 2; turn++) { int old = x; x = -z; z = old; }
        if ((heading & 1) == 0)
        {
            if (z < -1 || (z == -1 && Math.Abs(x) <= 1)) return 0;
            return z >= 0 && z <= 1 && Math.Abs(x) <= 1 ? 1 : 2;
        }
        int[] low = { 9, 1, 1, 0, -2, -2, -1 }, high = { -9, 2, 3, 3, 3, 2, 1 };
        if (x < low[z + 3] || x > high[z + 3]) return 0;
        int[,] red = { { 0, 0 }, { 1, -1 }, { -1, 1 }, { 1, 0 }, { 1, 1 }, { 0, 1 } };
        for (int i = 0; i < 6; i++) if (x == red[i, 0] && z == red[i, 1]) return 1;
        return 2;
    }
    private static int ForwardRegion(int x,int z,int heading,int radius,bool forward)
    {
        if(forward&&radius>1)
        {
            int a=x,b=z;for(int turn=0;turn<heading/2;turn++){int old=a;a=-b;b=old;}
            if((heading&1)==0)
            {if(b==2&&(a==-1||a==0||a==1))return 3;}
            else if((a==1&&b==2)||(a==2&&b==1)||(a==2&&b==2))return 3;
        }
        return Region(x,z,heading,radius);
    }
    private static float RegionDelay(int region,float inner,float outer,float ahead)
    {return region==1?inner:region==3?ahead:outer;}
    private static AuthorityGrid Grid(GridCoveragePattern shape = GridCoveragePattern.Rounded7x7, float inner = 0, float outer = 3,bool forward=false,float ahead=.2f)
    { return new AuthorityGrid(99, 2, shape, true, inner, outer,false,forward,ahead); }
    private static void MasksAndTimers()
    {
        foreach (int radius in new[] { 1, 2, 3 }) for (int heading = 0; heading < 8; heading++)
        {
            var shape = radius == 3 ? GridCoveragePattern.Rounded7x7 : radius == 2 ? GridCoveragePattern.Rounded5x5 : GridCoveragePattern.Square3x3;
            var grid = Grid(shape); var peers = Peers(P(1, yaw: heading * 45)); grid.Update(peers, 0);
            int red = 0, total = 0;
            for (int x = -4; x <= 4; x++) for (int z = -4; z <= 4; z++)
            {
                int region = Region(x, z, heading, radius); var cell = new Cell(x, z);
                if (region != 0) total++; if (region == 1) red++;
                Check("initial directional candidate matches independent row table", grid.Candidate(cell) == (region == 0 ? 0 : 1));
                Check("only six red fields mature immediately", grid.Owner(cell) == (region == 1 ? 1 : 0));
            }
            Check("every heading has exactly six red fields", red == 6 && grid.Changes.Count == 6);
            Check("entry visits only non-white fields", grid.NeighborReads == total && grid.CachedCells == total);
            if (radius == 3) Check("second sketch has 26 cardinal or 23 diagonal requested cells", total == ((heading & 1) == 0 ? 26 : 23));
            grid.Update(peers, 2.99f); Check("waiting timers are not scanned before deadline", grid.TimerVisits == 0);
            for (int x = -4; x <= 4; x++) for (int z = -4; z <= 4; z++) Check("yellow delay is not early", grid.Owner(new Cell(x, z)) == (Region(x, z, heading, radius) == 1 ? 1 : 0));
            grid.Update(peers, 3);
            for (int x = -4; x <= 4; x++) for (int z = -4; z <= 4; z++) Check("yellow matures without another player movement", grid.Owner(new Cell(x, z)) == (Region(x, z, heading, radius) == 0 ? 0 : 1));
            int reads = grid.NeighborReads, visits = grid.TimerVisits, updates = grid.PlayerUpdates;
            for (int i = 0; i < 1000; i++) grid.Update(peers, 4 + i);
            Check("standing players generate no new coverage or settled timer work", grid.NeighborReads == reads && grid.TimerVisits == visits && grid.PlayerUpdates == updates);
        }
        var custom = Grid(inner: 1.5f, outer: 5); var p = Peers(P(1));
        custom.Update(p, 0); custom.Update(p, 1.49f); Check("custom red wait respected", custom.Owner(new Cell(0, 0)) == 0);
        custom.Update(p, 1.5f); Check("custom red deadline", custom.Owner(new Cell(0, 0)) == 1 && custom.Owner(new Cell(0, 2)) == 0);
        custom.Update(p, 5); Check("custom outer deadline", custom.Owner(new Cell(0, 2)) == 1);
        var allNow = Grid(inner: 0, outer: 0); allNow.Update(p, 0); Check("both zero timers still exclude white", allNow.Changes.Count == 26 && allNow.Candidate(new Cell(0, -1)) == 0);
        var reversed = Grid(inner: 4, outer: 1); var pair = Peers(P(1), P(2, 2)); reversed.Update(pair, 0); reversed.Update(pair, 1);
        Check("mixed overlap uses shortest actual covering timer even with reversed config", reversed.Owner(new Cell(0, 0)) == 99 && reversed.Wait(new Cell(0, 0)) == 1);
        Check("all-red overlap still waits its configured inner delay", reversed.Owner(new Cell(1, 0)) == 0 && reversed.Wait(new Cell(1, 0)) == 4);
        reversed.Update(pair, 4); Check("custom all-red deadline completes", reversed.Owner(new Cell(1, 0)) == 99);
    }
    private static void SectorsAndIncrementalUpdates()
    {
        var grid = Grid(); grid.Update(Peers(P(1)), 0); int reads = grid.NeighborReads;
        foreach (float yaw in new[] { 1f, 20f, 22.499f, 0f, 359f, 337.501f }) grid.Update(Peers(P(1, yaw: yaw)), 1);
        Check("same north sector does no coverage work across wrap", grid.PlayerUpdates == 1 && grid.NeighborReads == reads);
        grid.Update(Peers(P(1, yaw: 22.5f)), 2); Check("sector boundary triggers even without additional 45-degree turn", grid.PlayerUpdates == 2);
        grid.Update(Peers(P(1, yaw: 67.499f)), 2.1f); Check("same diagonal sector is quiet", grid.PlayerUpdates == 2);
        grid.Update(Peers(P(1, yaw: 67.5f)), 2.2f); Check("next sector boundary triggers", grid.PlayerUpdates == 3);
        grid.Update(Peers(P(1, yaw: Single.NaN)), 2.3f); grid.Update(Peers(P(1, yaw: Single.PositiveInfinity)), 2.4f);
        Check("invalid replicated yaw retains last valid orientation", grid.PlayerUpdates == 3);
        grid.Update(Peers(P(1, x: 1, yaw: 80)), 2.5f); Check("cell movement triggers within same heading", grid.PlayerUpdates == 4);
        grid.Update(Peers(P(1, x: 1, yaw: -280)), 2.6f); Check("equivalent normalized angles are quiet", grid.PlayerUpdates == 4);

        grid = Grid(); grid.Update(Peers(P(1), P(2, 9)), 0); grid.Update(Peers(P(1), P(2, 9)), 3);
        reads = grid.NeighborReads; grid.Update(Peers(P(1, yaw: 20), P(2, 8)), 4);
        Check("moving player changes only boundary or timer-class contributions: " + (grid.NeighborReads - reads), grid.NeighborReads - reads == 20 && grid.PlayerUpdates == 3);
        grid.Update(Peers(P(1, yaw: 20), P(2, 2)), 5);
        Check("moving arrival intersects standing player's saved red field immediately", grid.Owner(new Cell(1, 0)) == 99);
        Check("far side of standing player remains its own", grid.Owner(new Cell(-2, 0)) == 1);
        Check("pure yellow overlap waits instead of creating global server ownership", grid.Owner(new Cell(1, 2)) != 99 && grid.Candidate(new Cell(1, 2)) == 99);
        grid.Update(Peers(P(1, yaw: 20), P(2, 2)), 8);
        Check("stationary overlaps mature with no new coverage work", grid.Owner(new Cell(1, 2)) == 99 && grid.PlayerUpdates == 4);
        grid.Update(Peers(P(1, yaw: 22.5f), P(2, 2)), 9); Check("other player's changes did not reset standing rotation sector", grid.PlayerUpdates == 5);
        grid.Update(Peers(P(1, yaw: 22.5f)), 10); grid.Update(Peers(P(1, yaw: 22.5f)), 13);
        Check("disconnect removes departed contributions only", grid.Owner(new Cell(1, 0)) == 1);
        grid.Update(Peers(), 14); grid.Update(Peers(), 17); Check("all unloaded peer policies released", grid.CachedCells == 0);

        grid = Grid(); grid.Update(Peers(P(0), P(99), P(1), P(1)), 0);
        Check("duplicate/invalid peers cannot make a false server overlap", grid.Owner(new Cell(0, 0)) == 1 && grid.PlayerUpdates == 1);
    }
    private sealed class ReferencePeer
    {
        internal Cell Cell; internal int Heading;
        internal readonly Dictionary<Cell, float> Entry = new Dictionary<Cell, float>();
    }
    private sealed class ReferencePolicy { internal long Candidate, Owner; internal float Since, Wait; }
    private static void RandomReference(bool forward=false,int radius=3,float inner=0,float outer=3,float ahead=.2f)
    {
        var random = new Random(83491); var grid = Grid(radius==3?GridCoveragePattern.Rounded7x7:GridCoveragePattern.Rounded5x5,inner,outer,forward,ahead);
        var cached = new Dictionary<long, ReferencePeer>(); var reference = new Dictionary<Cell, ReferencePolicy>();
        var input = new List<GridPeer>(); float last = 0;
        for (int step = 0; step < 400; step++)
        {
            float now = step < 200 ? step * .25f : (step - 120) * .25f;
            input.Clear();
            for (int id = 1; id <= 5; id++) if (random.Next(6) != 0) input.Add(P(id, random.Next(-4, 5), random.Next(-4, 5), random.Next(360)));
            var present = new HashSet<long>();
            foreach (var peer in input)
            {
                present.Add(peer.Uid); int heading = ((int)((peer.Yaw + 22.5f) / 45f) & 7); ReferencePeer old;
                if (!cached.TryGetValue(peer.Uid, out old) || !old.Cell.Equals(peer.Cell) || old.Heading != heading)
                {
                    var updated = new ReferencePeer { Cell = peer.Cell, Heading = heading };
                    for (int x = -radius; x <= radius; x++) for (int z = -radius; z <= radius; z++) if (ForwardRegion(x, z, heading, radius,forward) != 0)
                    {
                        var cell = new Cell(peer.Cell.X + x, peer.Cell.Z + z); float entry;
                        updated.Entry.Add(cell, old != null && old.Entry.TryGetValue(cell, out entry) ? entry : now);
                    }
                    cached[peer.Uid] = updated;
                }
            }
            var remove = new List<long>(); foreach (long id in cached.Keys) if (!present.Contains(id)) remove.Add(id); foreach (long id in remove) cached.Remove(id);
            grid.Update(input, now);
            for (int x = -7; x <= 7; x++) for (int z = -7; z <= 7; z++)
            {
                var cell = new Cell(x, z); long sole = 0; int count = 0; float wait=Single.PositiveInfinity;
                foreach (var peer in cached)
                {
                    int region = ForwardRegion(x - peer.Value.Cell.X, z - peer.Value.Cell.Z, peer.Value.Heading, radius,forward);
                    if (region == 0) continue; count++; sole = peer.Key; wait=Math.Min(wait,RegionDelay(region,inner,outer,ahead));
                }
                long candidate = count > 1 ? 99 : count == 1 ? sole : 0;
                ReferencePolicy policy; if (!reference.TryGetValue(cell, out policy)) { policy = new ReferencePolicy(); reference.Add(cell, policy); }
                bool rollback = now < last && policy.Owner != policy.Candidate;
                if (rollback)
                {
                    policy.Since = now;
                    foreach (var peer in cached) if (peer.Value.Entry.ContainsKey(cell)) peer.Value.Entry[cell] = now;
                }
                if (candidate != policy.Candidate)
                {
                    if (!(candidate == 99 && policy.Candidate != 0 && policy.Owner != policy.Candidate)) policy.Since = now;
                    policy.Candidate = candidate;
                }
                policy.Wait = count==0?outer:wait;
                float deadline = policy.Since + policy.Wait;
                if (count > 0)
                {
                    deadline = Single.PositiveInfinity;
                    foreach (var peer in cached)
                    {
                        int region = ForwardRegion(x - peer.Value.Cell.X, z - peer.Value.Cell.Z, peer.Value.Heading, radius,forward);
                        if (region != 0) deadline = Math.Min(deadline, Math.Max(policy.Since, Math.Min(now, peer.Value.Entry[cell])) + RegionDelay(region,inner,outer,ahead));
                    }
                }
                if (policy.Owner == candidate || now >= deadline) policy.Owner = candidate;
                Check("incremental candidate equals full independent reference", grid.Candidate(cell) == policy.Candidate);
                Check("incremental timed ownership equals full independent reference", grid.Owner(cell) == policy.Owner);
            }
            last = now;
        }
    }
    private sealed class Node : AuthorityNode
    {
        internal long Owner = 7; internal int Calls; internal bool Ready = true;
        internal override long CurrentOwner { get { return Owner; } }
        internal override ApplyResult Apply(long target) { Calls++; if (!Ready) return ApplyResult.Pending; if (target != 0) { Owner = target; Managed = true; ManagedOwner = target; } return ApplyResult.Done; }
    }
    private static void ObjectWork()
    {
        var world = new AuthorityWorld(99, 2, GridCoveragePattern.Rounded7x7, true, true);
        var nodes = new List<Node>(); for (int i = 0; i < 10000; i++) { var node = new Node { Cell = new Cell(0, i % 3) }; nodes.Add(node); world.Add(node, 0); }
        var p = Peers(P(1)); world.Tick(p, 0); Check("red object jobs use zero transition delay", nodes[0].Owner == 1 && nodes[1].Owner == 1 && nodes[2].Owner == 7);
        world.Tick(p, 3); Check("yellow objects complete after timer with standing peer", nodes[2].Owner == 1);
        int attempts = world.Attempts, reads = world.Grid.NeighborReads, visits = world.Grid.TimerVisits;
        for (int i = 0; i < 1000; i++) world.Tick(p, 4 + i);
        Check("10000 settled objects incur no repeated attempts/coverage/timer scans", world.Attempts == attempts && world.Grid.NeighborReads == reads && world.Grid.TimerVisits == visits);
        nodes[0].Owner = 17; world.Changed(nodes[0], 1004); world.Tick(p, 1004); Check("red owner-event reconciliation adds no legacy two-second wait", nodes[0].Owner == 1);
        var guarded = new Node { Cell = new Cell(0, 0), Ready = false }; world.Add(guarded, 1005); world.Tick(p, 1005);
        Check("zero field timer never bypasses a genuine pending guard", guarded.Owner == 7 && world.PendingCount == 1);
        guarded.Ready = true; world.Tick(p, 1006); Check("ready guard resumes on next configured tick", guarded.Owner == 1 && world.PendingCount == 0);
        attempts = world.Attempts; world.Tick(Peers(P(1, yaw: 180)), 1007); world.Tick(Peers(P(1, yaw: 180)), 1010);
        Check("now-white fields retain actual owner without return jobs", nodes[1].Owner == 1 && nodes[2].Owner == 1 && world.Attempts == attempts);
        world.Tick(Peers(), 1011); world.Tick(Peers(), 1014); Check("empty directional coverage releases cell cache", world.Grid.CachedCells == 0);
        foreach (Node node in nodes) world.Remove(node); world.Remove(guarded); Check("object and pending references cleaned", world.BucketCount == 0 && world.PendingCount == 0);
        world = new AuthorityWorld(99, 2, GridCoveragePattern.Rounded7x7, true, true);
        var promoted = new Node { Cell = new Cell(2, 0) }; world.Add(promoted, 0); world.Tick(p, 0); world.Tick(p, 3);
        promoted.Owner = 17; world.Changed(promoted, 3.5f); world.Tick(Peers(P(1, 1)), 4);
        Check("yellow-to-red promotion accelerates queued debounce without owner-choice change", promoted.Owner == 1);
        promoted.Owner = 17; promoted.Ready = false; world.Changed(promoted, 4.1f); world.Tick(Peers(P(1, 1)), 4.2f);
        Check("accelerated reconciliation still obeys genuine pending readiness", promoted.Owner == 17);
    }
    private static void ForwardMasksAndTimers()
    {
        foreach(int radius in new[]{2,3})for(int heading=0;heading<8;heading++)
        {
            var shape=radius==2?GridCoveragePattern.Rounded5x5:GridCoveragePattern.Rounded7x7;
            var grid=Grid(shape,forward:true);var input=Peers(P(1,yaw:heading*45));grid.Update(input,0);
            int red=0,ahead=0,total=0;
            for(int x=-4;x<=4;x++)for(int z=-4;z<=4;z++)
            {
                int region=ForwardRegion(x,z,heading,radius,true);var cell=new Cell(x,z);
                if(region!=0)total++;if(region==1)red++;if(region==3)ahead++;
                Check("forward mask candidate equals independent table",grid.Candidate(cell)==(region==0?0:1));
                Check("forward row is not initially red",grid.Owner(cell)==(region==1?1:0));
                if(region!=0)Check("all three timer classes have independent waits",grid.Wait(cell)==RegionDelay(region,0,3,.2f));
            }
            Check("forward priority has six inner and exactly three extra fields",red==6&&ahead==3);
            Check("only 5x5 diagonal gains its forward tip",total==(radius==2?15:((heading&1)==0?26:23))&&grid.CachedCells==total);
            grid.Update(input,.199f);
            for(int x=-4;x<=4;x++)for(int z=-4;z<=4;z++)Check("forward wait is not early",grid.Owner(new Cell(x,z))==(ForwardRegion(x,z,heading,radius,true)==1?1:0));
            grid.Update(input,.2f);
            for(int x=-4;x<=4;x++)for(int z=-4;z<=4;z++){int region=ForwardRegion(x,z,heading,radius,true);Check("one new forward row matures after 0.2",grid.Owner(new Cell(x,z))==(region==1||region==3?1:0));}
            grid.Update(input,3);
            for(int x=-4;x<=4;x++)for(int z=-4;z<=4;z++)Check("remaining outer fields keep their timer",grid.Owner(new Cell(x,z))==(ForwardRegion(x,z,heading,radius,true)==0?0:1));
            int reads=grid.NeighborReads,visits=grid.TimerVisits;
            for(int i=0;i<1000;i++)grid.Update(input,4+i);
            Check("settled third tier adds no repeated coverage or timer work",grid.NeighborReads==reads&&grid.TimerVisits==visits);
        }
        var tiny=Grid(GridCoveragePattern.Square3x3,forward:true,ahead:0);tiny.Update(Peers(P(1)),0);
        Check("pure 3x3 remains 3x2 with no unsupported extra row",tiny.CachedCells==6&&tiny.Changes.Count==6&&tiny.Candidate(new Cell(0,2))==0);
        var zero=Grid(forward:true,ahead:0);zero.Update(Peers(P(1)),0);
        Check("zero forward timer gives nine priority fields without enabling rear",zero.Changes.Count==9&&zero.Owner(new Cell(0,2))==1&&zero.Candidate(new Cell(0,-1))==0&&zero.Owner(new Cell(0,3))==0);
        var custom=Grid(inner:1,outer:4,forward:true,ahead:.7f);custom.Update(Peers(P(1)),0);custom.Update(Peers(P(1)),.7f);
        Check("custom forward timer is independent of red and yellow",custom.Owner(new Cell(0,2))==1&&custom.Owner(new Cell(0,0))==0&&custom.Owner(new Cell(0,3))==0);
        var invalid=Grid(forward:true,ahead:Single.NaN);invalid.Update(Peers(P(1)),0);Check("invalid forward delay uses 0.2 fallback",invalid.Wait(new Cell(0,2))==.2f);

        var overlap=Grid(forward:true);overlap.Update(Peers(P(1)),0);overlap.Update(Peers(P(1),P(2,3,2)),.1f);
        Check("waiting forward overlap never assigns lone player first",overlap.Owner(new Cell(0,2))==0&&overlap.Candidate(new Cell(0,2))==99);
        overlap.Update(Peers(P(1),P(2,3,2)),.2f);Check("overlap targets server at earliest forward deadline",overlap.Owner(new Cell(0,2))==99);
        var reversed=Grid(inner:2,outer:1,forward:true,ahead:5);var pair=Peers(P(1),P(2,3,2));reversed.Update(pair,0);reversed.Update(pair,1);
        Check("third-tier mixed overlap supports reversed timer order",reversed.Owner(new Cell(0,2))==99&&reversed.Wait(new Cell(0,2))==1);
        reversed=Grid(inner:2,outer:1,forward:true,ahead:5);pair=Peers(P(1),P(2));reversed.Update(pair,0);reversed.Update(pair,1);
        Check("all-forward overlap does not use unrelated outer timer",reversed.Owner(new Cell(0,2))==0&&reversed.Wait(new Cell(0,2))==5);
        reversed.Update(pair,5);Check("custom all-forward overlap completes",reversed.Owner(new Cell(0,2))==99);
        var promotion=Grid(forward:true);promotion.Update(Peers(P(1)),0);promotion.Update(Peers(P(1,z:1)),.1f);
        Check("forward to inner promotion keeps entry and commits immediately",promotion.Owner(new Cell(0,2))==1&&promotion.Wait(new Cell(0,2))==0);
        promotion.Update(Peers(P(1,z:1,yaw:90)),.15f);promotion.Update(Peers(P(1,z:1,yaw:90)),3.15f);
        Check("rotation removes old tier and has no stale forward counter",promotion.Wait(new Cell(2,1))==.2f&&promotion.Owner(new Cell(2,1))==1);

        var world=new AuthorityWorld(99,2,GridCoveragePattern.Rounded7x7,true,true,0,3,true,.2f);
        var nodes=new List<Node>();for(int i=0;i<10000;i++){var node=new Node{Cell=new Cell(0,2)};nodes.Add(node);world.Add(node,0);}
        world.Tick(Peers(P(1)),0);world.Tick(Peers(P(1)),.2f);int attempts=world.Attempts;
        for(int i=0;i<1000;i++)world.Tick(Peers(P(1)),1+i);
        Check("10000 forward objects do not repeat jobs after completion",world.Attempts==attempts&&nodes[0].Owner==1&&world.Grid.RememberedCells==1);
        world.Tick(Peers(),1002);Check("vacated third-tier coverage is immediately cleared",world.Grid.CachedCells==0&&world.Grid.ReusablePolicies<=1024);
        foreach(var node in nodes)world.Remove(node);
        Check("forward certificate and bucket vanish on last unload",world.Grid.RememberedCells==0&&world.BucketCount==0&&world.PendingCount==0);
        world.Tick(Peers(P(2)),1003);Check("reused third-tier policies forget old owner and counters",world.Grid.Owner(new Cell(0,0))==2&&world.Grid.Wait(new Cell(0,2))==.2f);
        var guarded=new Node{Cell=new Cell(0,2),Ready=false};world.Add(guarded,1003);world.Tick(Peers(P(2)),1003.25f);
        Check("third timer never overrides collider or native readiness guard",guarded.Owner==7&&world.PendingCount==1);
        guarded.Ready=true;world.Tick(Peers(P(2)),1003.5f);Check("forward readiness resumes on next tick",guarded.Owner==2&&world.PendingCount==0);
    }
    public static void Main()
    {
        try
        {
        MasksAndTimers(); SectorsAndIncrementalUpdates(); RandomReference(); ObjectWork();ForwardMasksAndTimers();
        foreach(int radius in new[]{2,3}){RandomReference(true,radius);RandomReference(true,radius,1,4,.7f);RandomReference(true,radius,2,1,5);}
        Console.WriteLine("PASS: " + checks + " directional grid assertions; eight sector masks, independent randomized reference, standing overlaps and 10000-object/1000-cycle workload.");
        }
        catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
    }
}
