using System;
using System.Collections.Generic;
using TooManyLagsDedicatedAddon;

internal static class FieldCacheTests
{
    private static int checks;
    private static void Check(string message, bool ok) { checks++; if (!ok) throw new Exception(message); }
    private static readonly Cell Yellow = new Cell(0, 2);
    private static List<GridPeer> Peers(params GridPeer[] peers) { return new List<GridPeer>(peers); }
    private static GridPeer P(long uid, int x = 0, int z = 0, float yaw = 0) { return new GridPeer(uid, new Cell(x, z), yaw); }
    private static AuthorityWorld World(bool keep = true) { return new AuthorityWorld(99, 2, GridCoveragePattern.Rounded7x7, keep, true); }
    private sealed class Node : AuthorityNode
    {
        internal long Owner = 7; internal int Calls, Writes; internal bool Ready = true, Pinned, Drop;
        internal override long CurrentOwner { get { return Owner; } }
        internal override ApplyResult Apply(long target)
        {
            Calls++; if (Drop) return ApplyResult.Dropped;
            if (!Ready) return ApplyResult.Pending;
            if (target != 0) { target = Pinned ? 99 : target; if (Owner != target) { Owner = target; Writes++; } Managed = true; ManagedOwner = target; }
            return ApplyResult.Done;
        }
        internal override bool CanRemember(long target) { return Managed && ManagedOwner == Owner && Owner == (Pinned ? 99 : target); }
    }
    private static Node Add(AuthorityWorld world, Cell cell, float now = 0)
    { var node = new Node { Cell = cell }; world.Add(node, now); return node; }
    private static void RememberedReentry()
    {
        foreach (bool keep in new[] { true, false })
        {
            var world = World(keep); var node = Add(world, Yellow); var a = Peers(P(1));
            world.Tick(a, 0); world.Tick(a, 3);
            Check("completed yellow field remembered only when enabled", world.Grid.RememberedCells == (keep ? 1 : 0));
            int attempts = world.Attempts;
            var outside = Peers(P(1, x: keep ? 0 : 10, yaw: 180));
            world.Tick(outside, 4); world.Tick(outside, 7);
            Check("field is vacated by turning with the rear cutout or moving outside the full shape", world.Grid.Candidate(Yellow) == 0);
            world.Tick(a, 8);
            Check("remembered player skips new outer timer", world.Grid.Owner(Yellow) == (keep ? 1 : 0));
            world.Tick(a, 11);
            Check("same owner reentry does no object job when cached", world.Attempts == attempts + (keep ? 0 : 2));
            Check("cache never adds an actual owner write", node.Writes == 1);
            if (keep) Check("one loaded cell skipped", world.SkippedCells == 1);
            world.Remove(node);
            Check("last unloaded object forgets field certificate", world.Grid.RememberedCells == 0 && world.BucketCount == 0);
        }
    }
    private static void ForeignRequestsAndServers()
    {
        var world = World(); var node = Add(world, Yellow);
        world.Tick(Peers(P(1)), 0); world.Tick(Peers(P(1)), 3);
        world.Tick(Peers(P(1), P(2)), 4);
        Check("foreign coverage immediately invalidates cache before its timer", world.Grid.RememberedCells == 0 && node.Owner == 1);
        world.Tick(Peers(P(1), P(2)), 7);
        Check("standing first player remains an overlap peer", node.Owner == 99 && world.Grid.RememberedCells == 0);
        world.Tick(Peers(P(1, yaw: 180), P(2, yaw: 180)), 8);
        world.Tick(Peers(P(1, yaw: 180), P(2, yaw: 180)), 11);
        int attempts = world.Attempts;
        world.Tick(Peers(P(1), P(2)), 12);
        Check("server field has no reentry lock", world.Grid.Owner(Yellow) == 0);
        world.Tick(Peers(P(1), P(2)), 15);
        Check("server field repeats normal mature request", world.Attempts == attempts + 1 && node.Owner == 99);
        world.Tick(Peers(P(1)), 16); world.Tick(Peers(P(1)), 19);
        Check("outer server field remains held for a stationary lone player", node.Owner == 99 && world.Grid.RememberedCells == 0);
        world.Tick(Peers(P(1, yaw:45)),20);world.Tick(Peers(P(1, yaw:45)),23);
        Check("real heading change releases held outer field with its normal timer",node.Owner==1&&world.Grid.RememberedCells==1);

        world = World(); node = Add(world, Yellow); world.Tick(Peers(P(1)), 0); world.Tick(Peers(P(1)), 3);
        world.Tick(Peers(P(1), P(2)), 4); world.Tick(Peers(P(1)), 4.5f);
        Check("even an uncompleted foreign request releases remembered field", world.Grid.RememberedCells == 0);
        world.Tick(Peers(), 5); world.Tick(Peers(), 8); attempts = world.Attempts;
        world.Tick(Peers(P(1)), 9); Check("former owner must request again after a foreign visit", world.Grid.Owner(Yellow) == 0);
        world.Tick(Peers(P(1)), 12); Check("one new bucket check but no redundant owner write", world.Attempts == attempts + 1 && node.Writes == 1);
    }
    private static void InnerOnlyDeparture()
    {
        int[] fx={0,1,1,1,0,-1,-1,-1},fz={1,1,0,-1,-1,-1,0,1};
        foreach(GridCoveragePattern shape in new[]{GridCoveragePattern.Rounded5x5,GridCoveragePattern.Rounded7x7})
        foreach(bool keep in new[]{true,false})for(int heading=0;heading<8;heading++)
        {
            var world=new AuthorityWorld(99,2,shape,keep,true,0,3,true,.2f);
            var red=Add(world,new Cell(0,0));var orange=Add(world,new Cell(2*fx[heading],2*fz[heading]));
            bool diagonal=fx[heading]!=0&&fz[heading]!=0;
            var yellow=Add(world,diagonal?new Cell(2*fx[heading],0):new Cell(2*fz[heading],-2*fx[heading]));
            var a=P(1,yaw:heading*45);var both=Peers(a,P(2,yaw:heading*45));
            world.Tick(both,0);world.Tick(both,3);
            Check("all actual covering tiers first reach server overlap",red.Owner==99&&orange.Owner==99&&yellow.Owner==99);
            world.Tick(Peers(a),4);
            Check("only remaining player's red field transfers immediately",red.Owner==1&&orange.Owner==99&&yellow.Owner==99);
            world.Tick(Peers(a),7);
            Check("orange and yellow departure retention requires keep-on-exit",orange.Owner==(keep?99:1)&&yellow.Owner==(keep?99:1));
            int attempts=world.Attempts,visits=world.Grid.TimerVisits,builds=world.Grid.Rebuilds;
            for(int sample=1;sample<=1000;sample++)world.Tick(Peers(a),7+sample*.1f);
            Check("standing held fields add no jobs timer visits or field rebuilds",world.Attempts==attempts&&world.Grid.TimerVisits==visits&&world.Grid.Rebuilds==builds);
            Check("settled held fields retain no pending timer or object job",world.PendingCount==0&&world.Grid.DeferredCells==(keep?world.Grid.CachedCells-6:0));
            world.Remove(red);world.Remove(orange);world.Remove(yellow);world.Tick(Peers(),108);world.Tick(Peers(),111);
            Check("departure and unload release every deferred policy and reference",world.Grid.DeferredCells==0&&world.Grid.CachedCells==0&&world.BucketCount==0&&world.PendingCount==0);
        }
        // An inner delay may be configured above the other delays; the rule
        // follows the red tier, never an unrelated minimum numeric value.
        var reversed=new AuthorityWorld(99,2,GridCoveragePattern.Rounded7x7,true,true,2,0,true,0);
        var inner=Add(reversed,new Cell(0,0));var outer=Add(reversed,Yellow);var forward=Add(reversed,new Cell(0,2));
        reversed.Tick(Peers(P(1),P(2)),0);reversed.Tick(Peers(P(1),P(2)),2);reversed.Tick(Peers(P(1)),3);reversed.Tick(Peers(P(1)),4.99f);
        Check("inner-only departure still respects a nonzero inner guard timer",inner.Owner==99&&outer.Owner==99&&forward.Owner==99);
        reversed.Tick(Peers(P(1)),5);Check("inner tier transfers while zero-delay outer tiers stay held",inner.Owner==1&&outer.Owner==99&&forward.Owner==99);
        var full=new AuthorityWorld(99,2,GridCoveragePattern.Rounded7x7,true,false);
        var normal=Add(full,new Cell(0,0));full.Tick(Peers(P(1),P(2)),0);full.Tick(Peers(P(1),P(2)),2);full.Tick(Peers(P(1)),3);full.Tick(Peers(P(1)),5);
        Check("full-grid mode retains the existing departure behavior",normal.Owner==1&&full.Grid.DeferredCells==0);
        var small=new AuthorityWorld(99,2,GridCoveragePattern.Square3x3,true,true);
        normal=Add(small,new Cell(1,1));small.Tick(Peers(P(1),P(2)),0);small.Tick(Peers(P(1)),1);
        Check("cardinal directional 3x3 has only the six inner fields",normal.Owner==1&&small.Grid.DeferredCells==0);
    }
    private static void DeferredRefreshAndLifetime()
    {
        var world=new AuthorityWorld(99,2,GridCoveragePattern.Rounded7x7,true,true,0,3,true,.2f);
        var yellow=Add(world,new Cell(2,0));var orange=Add(world,new Cell(0,2));var red=Add(world,new Cell(0,0));
        world.Tick(Peers(P(1),P(2)),0);world.Tick(Peers(P(1),P(2)),3);world.Tick(Peers(P(1)),4);
        int attempts=world.Attempts;world.Tick(Peers(P(1,yaw:22.4f)),5);world.Tick(Peers(P(1,yaw:22.4f)),20);
        Check("sub-sector turns cannot release deferred outer fields",yellow.Owner==99&&orange.Owner==99&&world.Attempts==attempts);
        world.Tick(Peers(P(1,0,1)),21);
        Check("movement promotes held forward row into red immediately",orange.Owner==1&&yellow.Owner==99);
        world.Tick(Peers(P(1,0,1)),23.99f);Check("same-tier retained coverage starts its fresh outer timer",yellow.Owner==99);
        world.Tick(Peers(P(1,0,1)),24);Check("cell change releases unchanged-tier outer coverage",yellow.Owner==1&&world.Grid.DeferredCells==0);

        foreach(bool reverse in new[]{true,false})
        {
            world=World();yellow=Add(world,new Cell(2,0));
            world.Tick(Peers(P(1),P(2)),0);world.Tick(Peers(P(1),P(2)),3);
            var moving=P(1,yaw:45);var leaving=P(2,10);
            world.Tick(reverse?Peers(leaving,moving):Peers(moving,leaving),4);world.Tick(reverse?Peers(leaving,moving):Peers(moving,leaving),7);
            Check("simultaneous survivor refresh and departure are order independent",yellow.Owner==1&&world.Grid.Candidate(yellow.Cell)==1);
        }
        world=World();yellow=Add(world,Yellow);world.Tick(Peers(P(1),P(2),P(3)),0);world.Tick(Peers(P(1),P(2),P(3)),3);
        world.Tick(Peers(P(1),P(2)),4);Check("three-to-two retains true overlap without deferral",yellow.Owner==99&&world.Grid.DeferredCells==0);
        world.Tick(Peers(P(1)),5);int held=world.Grid.DeferredCells;world.Tick(Peers(P(1),P(4)),6);
        Check("new player releases all retained overlap fields",held>0&&world.Grid.DeferredCells==0&&yellow.Owner==99);
        world.Tick(Peers(P(4)),7);world.Tick(Peers(P(4)),20);
        Check("subsequent departure keeps the new stationary lone player deferred",yellow.Owner==99&&world.Grid.DeferredCells>0);

        // A briefly overlapping request must not leave a timer that switches
        // to server or player after the overlap has already disappeared.
        var grid=new AuthorityGrid(99,2,GridCoveragePattern.Rounded7x7,true,0,3,true);
        grid.Update(Peers(P(1)),0);grid.Update(Peers(P(1),P(2)),1);grid.Update(Peers(P(1)),1.5f);grid.Update(Peers(P(1)),30);
        Check("uncompleted outer overlap cancels its timer and retains settled owner",grid.Owner(Yellow)==0&&grid.Candidate(Yellow)==0&&grid.DeferredCells>0);
        grid.Update(Peers(P(1,yaw:45)),31);grid.Update(Peers(P(1,yaw:45)),33.99f);Check("released uncompleted request starts a new full timer",grid.Owner(Yellow)==0);
        grid.Update(Peers(P(1,yaw:45)),34);Check("real survivor refresh releases canceled outer overlap",grid.Owner(Yellow)==1&&grid.DeferredCells==0);

        world=World();red=Add(world,new Cell(0,0));yellow=Add(world,Yellow);world.Tick(Peers(P(1),P(2)),0);world.Tick(Peers(P(1),P(2)),3);
        red.Ready=false;world.Tick(Peers(P(1)),4);world.Tick(Peers(P(1)),20);
        Check("inner-only departure cannot bypass an existing readiness guard",red.Owner==99&&yellow.Owner==99&&world.PendingCount==1);
        red.Ready=true;world.Tick(Peers(P(1)),21);Check("ready guarded red field transfers without waking outer fields",red.Owner==1&&yellow.Owner==99);

        world=World();
        for(int visit=0;visit<2000;visit++)
        {
            int x=visit*10;float now=visit*10;var node=Add(world,new Cell(x,2),now);
            world.Tick(Peers(P(1,x),P(2,x)),now);world.Tick(Peers(P(1,x),P(2,x)),now+3);world.Tick(Peers(P(1,x)),now+4);
            Check("only current loaded coverage owns deferred state",world.Grid.DeferredCells>0&&world.Grid.DeferredCells<=world.Grid.CachedCells);
            world.Remove(node);world.Tick(Peers(),now+5);
            Check("reused policies have no deferred cells after complete departure",world.Grid.DeferredCells==0&&world.Grid.CachedCells==0&&world.Grid.RememberedCells==0&&world.BucketCount==0&&world.PendingCount==0);
            Check("policy reuse remains capped",world.Grid.ReusablePolicies<=1024);
        }
    }
    private static void PendingTimers()
    {
        var grid = new AuthorityGrid(99, 2, GridCoveragePattern.Rounded7x7, true);
        grid.Update(Peers(P(1)), 0); grid.Update(Peers(P(1), P(2)), 2);
        Check("waiting lone request retargeted to server", grid.Candidate(Yellow) == 99 && grid.Owner(Yellow) == 0);
        grid.Update(Peers(P(1), P(2)), 2.99f); Check("earliest request timer is not early", grid.Owner(Yellow) == 0);
        grid.Update(Peers(P(1), P(2)), 3);
        Check("first timer commits server directly instead of first player", grid.Owner(Yellow) == 99);

        grid = new AuthorityGrid(99, 2, GridCoveragePattern.Rounded7x7, true);
        grid.Update(Peers(P(1)), 0); grid.Update(Peers(P(1), P(2)), 2);
        grid.Update(Peers(P(2)), 2.5f); grid.Update(Peers(P(2)), 3);
        Check("departed timer source cannot grant lone newcomer early", grid.Owner(Yellow) == 0);
        grid.Update(Peers(P(2)), 5.5f); Check("lone departure target observes normal grace", grid.Owner(Yellow) == 2);

        grid = new AuthorityGrid(99, 2, GridCoveragePattern.Rounded7x7, true);
        grid.Update(Peers(P(1)), 0); grid.Update(Peers(P(1), P(2)), 2); grid.Update(Peers(P(1), P(2), P(3)), 2.5f);
        grid.Update(Peers(P(2), P(3)), 2.75f); grid.Update(Peers(P(2), P(3)), 3);
        Check("removed oldest request cannot mature a remaining overlap", grid.Owner(Yellow) == 0);
        grid.Update(Peers(P(2), P(3)), 5); Check("next surviving timer matures remaining overlap", grid.Owner(Yellow) == 99);

        var world = World(); var node = Add(world, Yellow);
        world.Tick(Peers(P(1)), 0); world.Tick(Peers(P(1), P(2)), 2); world.Tick(Peers(P(1), P(2)), 3);
        Check("actual job writes only server, with no intermediate player owner", node.Owner == 99 && node.Writes == 1);
        world = World(); node = Add(world, Yellow); node.Ready = false;
        world.Tick(Peers(P(1)), 0); world.Tick(Peers(P(1)), 3); world.Tick(Peers(P(1), P(2)), 4);
        node.Ready = true; world.Tick(Peers(P(1), P(2)), 7);
        Check("pending readiness guard follows settled overlap target", node.Owner == 99 && node.Writes == 1);

        grid = new AuthorityGrid(99, 2, GridCoveragePattern.Rounded7x7, true);
        grid.Update(Peers(P(1)), 10); grid.Update(Peers(P(1), P(2)), 12); grid.Update(Peers(P(1), P(2)), 1);
        grid.Update(Peers(P(1), P(2)), 3.99f); Check("rollback safely restarts valid timers", grid.Owner(Yellow) == 0);
        grid.Update(Peers(P(1), P(2)), 4); Check("rollback timer completes", grid.Owner(Yellow) == 99);
        grid = new AuthorityGrid(99, 2, GridCoveragePattern.Rounded7x7, true);
        grid.Update(Peers(P(1)), 10); grid.Update(Peers(P(1)), 1); grid.Update(Peers(P(1), P(2, 2)), 2);
        Check("rollback cannot turn an existing inner zero-timer peer into a future request", grid.Owner(new Cell(0, 0)) == 99);
    }
    private static void ObjectEventsAndGuards()
    {
        var world = World(); var node = Add(world, new Cell(0, 0)); var a = Peers(P(1)); world.Tick(a, 0);
        Check("initial inner field certified", world.Grid.RememberedCells == 1);
        node.Owner = 17; world.Changed(node, 1);
        Check("external owner invalidates immediately", world.Grid.RememberedCells == 0);
        world.Tick(a, 1); Check("external owner repaired and certificate restored", node.Owner == 1 && world.Grid.RememberedCells == 1);
        node.Owner = 17; world.Received(node, 2); Check("handoff receipt also invalidates", world.Grid.RememberedCells == 0);
        world.Tick(a, 2); Check("handoff maintenance uses current desired owner", node.Owner == 1);
        int calls = node.Calls; var added = Add(world, node.Cell, 3); added.Ready = false; world.Tick(a, 3);
        Check("adding object does not rescan unrelated settled objects", node.Calls == calls && world.Grid.RememberedCells == 0);
        world.Tick(a, 4); Check("unfinished object forbids certifying whole field", world.Grid.RememberedCells == 0 && added.Owner == 7);
        added.Ready = true; world.Tick(a, 5); Check("completed last object certifies in constant work", world.Grid.RememberedCells == 1);
        world.Move(added, Yellow, 6); Check("moving animal is removed from old certificate membership", world.Grid.IsRemembered(node.Cell, 1));
        world.Tick(a, 9); Check("moved animal reaches valid destination", added.Owner == 1);
        world.Remove(node); world.Remove(added); Check("all cache/index/work references released", world.Grid.RememberedCells == 0 && world.PendingCount == 0 && world.BucketCount == 0);

        world = World(); node = Add(world, Yellow); node.Pinned = true; world.Tick(a, 0); world.Tick(a, 3);
        Check("permanent refill server ownership is compatible with field certificate", node.Owner == 99 && world.Grid.RememberedCells == 1);
        calls = node.Calls; world.Tick(Peers(), 4); world.Tick(Peers(), 7); world.Tick(a, 8);
        Check("pinned refill server object is never handed to player by cache", node.Owner == 99 && node.Calls == calls);

        world = World(); node = Add(world, Yellow); node.Drop = true; world.Tick(a, 0); world.Tick(a, 3);
        Check("dropped/unmanaged object cannot certify field", world.Grid.RememberedCells == 0);
    }
    private static void UnloadAndPeerIds()
    {
        var world = World(); var input = new List<GridPeer>();
        for (int i = 0; i < 12; i++) input.Add(P(Int64.MaxValue - 17 * i, i * 10));
        for (int region = 0; region < 2000; region++)
        {
            int x = region * 10; var node = Add(world, new Cell(x, 0), region * 10);
            world.Tick(Peers(P(Int64.MaxValue, x)), region * 10); world.Tick(Peers(P(Int64.MaxValue, x)), region * 10 + 3);
            Check("one visited loaded cell stores at most one certificate", world.Grid.RememberedCells <= 1);
            world.Remove(node); Check("unload does not leave historical cell cache", world.Grid.RememberedCells == 0 && world.BucketCount == 0);
        }
        world.Tick(input, 30000); world.Tick(input, 30003);
        for (int i = 0; i < 12; i++) Check("native large UIDs keep distinct peers without remapped slots", world.Grid.Owner(new Cell(i * 10, 0)) == input[i].Uid);
        input.Clear(); world.Tick(input, 30004); world.Tick(input, 30007);
        Check("active policies cleaned after all peers depart", world.Grid.CachedCells == 0);
    }
    private static void VacantPolicyLifetime()
    {
        foreach(GridCoveragePattern shape in new[]{GridCoveragePattern.Square3x3,GridCoveragePattern.Rounded5x5,GridCoveragePattern.Rounded7x7})
        foreach(bool directional in new[]{false,true})
        {
            var world=new AuthorityWorld(99,30,shape,true,directional,0,30);
            var node=Add(world,new Cell(0,0));var input=new List<GridPeer>();
            for(int id=1;id<=12;id++)input.Add(P(id,(id-1)*100));
            world.Tick(input,0);world.Tick(input,30);
            Check("loaded object completed before testing exit",node.Owner==1&&world.Grid.RememberedCells==1);
            int attempts=world.Attempts,writes=node.Writes;
            input.Clear();for(int id=1;id<=12;id++)input.Add(P(id,(id-1)*100+8));world.Tick(input,30.1f);
            Check("fully vacated cells need no 30-second retirement policy",world.Grid.Owner(node.Cell)==0&&world.Grid.Candidate(node.Cell)==0);
            Check("immediate logical release neither changes physical owner nor schedules jobs",node.Owner==1&&node.Writes==writes&&world.Attempts==attempts&&world.Grid.RememberedCells==1);
            int limit=12*(shape==GridCoveragePattern.Rounded7x7?37:shape==GridCoveragePattern.Rounded5x5?21:9);
            for(int frame=1;frame<=1000;frame++)
            {
                input.Clear();for(int id=1;id<=12;id++)input.Add(P(id,(id-1)*100+(frame%200)*8-800));
                world.Tick(input,31+frame*.1f);
                Check("twelve moving peers cannot retain abandoned policies even with max delays",world.Grid.CachedCells<=limit);
                Check("travel certificate storage remains limited by loaded object buckets",world.Grid.RememberedCells<=world.BucketCount);
                Check("policy allocations do not grow with travelled distance",world.Grid.PolicyAllocations<=limit*2);
                Check("empty allocation pool is capped",world.Grid.ReusablePolicies<=1024);
            }
            world.Tick(Peers(),132);
            Check("all temporary coverage disappears immediately on full departure",world.Grid.CachedCells==0);
            world.Remove(node);Check("last unload releases historical loaded certificate",world.Grid.RememberedCells==0&&world.BucketCount==0&&world.PendingCount==0);
        }
        var legacy=new AuthorityWorld(99,30,GridCoveragePattern.Square3x3,false);
        var tracked=Add(legacy,new Cell(0,0));legacy.Tick(Peers(P(1)),0);legacy.Tick(Peers(P(1)),30);legacy.Tick(Peers(),31);
        Check("return-enabled policy still retains its normal retirement grace",legacy.Grid.Owner(tracked.Cell)==1);
        legacy.Tick(Peers(),61);Check("return-enabled policy still expires normally",legacy.Grid.CachedCells==0);
        var reuse=World();reuse.Tick(Peers(P(1),P(2)),0);reuse.Tick(Peers(),1);
        Check("released overlap policies have no cell identity",reuse.Grid.CachedCells==0&&reuse.Grid.ReusablePolicies>0);
        reuse.Tick(Peers(P(3,100)),2);
        Check("reused overlap storage cannot retain old owners/counts/timers",reuse.Grid.Owner(new Cell(100,0))==3&&reuse.Grid.Candidate(new Cell(100,2))==3&&reuse.Grid.Owner(new Cell(100,2))==0);
        reuse.Tick(Peers(P(3,100)),5);Check("reused outer request respects fresh timer",reuse.Grid.Owner(new Cell(100,2))==3);
    }
    private static void RandomObjectEvents()
    {
        var random = new Random(31071); var world = World(); var nodes = new List<Node>();
        for (int i = 0; i < 70; i++) nodes.Add(Add(world, new Cell(random.Next(-6, 7), random.Next(-6, 7))));
        var peers = new List<GridPeer>();
        for (int step = 0; step < 8000; step++)
        {
            float now = step * 4; peers.Clear();
            for (int id = 1; id <= 5; id++) if (random.Next(5) != 0) peers.Add(P(id,random.Next(-5,6),random.Next(-5,6),random.Next(8)*45));
            Node node = nodes[random.Next(nodes.Count)];
            switch (random.Next(6))
            {
                case 0: world.Move(node,new Cell(random.Next(-6,7),random.Next(-6,7)),now); break;
                case 1: node.Owner=random.Next(1,6);world.Changed(node,now);break;
                case 2: node.Owner=99;world.Received(node,now);break;
                case 3: node.Ready=!node.Ready;world.Changed(node,now);break;
                case 4: world.Remove(node);nodes.Remove(node);nodes.Add(Add(world,new Cell(random.Next(-6,7),random.Next(-6,7)),now));break;
            }
            world.Tick(peers,now);
            Check("random cache never outgrows loaded buckets",world.Grid.RememberedCells<=world.BucketCount);
            foreach(Node member in nodes)
            {
                long desired=world.Grid.Owner(member.Cell);
                if(world.Grid.IsRemembered(member.Cell,desired))
                    Check("every randomized certified object still has its actual promised owner",member.Managed&&member.ManagedOwner==desired&&member.Owner==desired);
            }
        }
        foreach(Node node in nodes)world.Remove(node);
        Check("random unload clears all cached work and references",world.Grid.RememberedCells==0&&world.BucketCount==0&&world.PendingCount==0);
    }
    public static void Main()
    {
        RememberedReentry(); ForeignRequestsAndServers(); InnerOnlyDeparture(); DeferredRefreshAndLifetime(); PendingTimers(); ObjectEventsAndGuards(); UnloadAndPeerIds(); VacantPolicyLifetime(); RandomObjectEvents();
        Console.WriteLine("PASS: " + checks + " field-cache assertions; guarded completion, foreign invalidation, earliest valid overlap timers, native UIDs and 2000 unloaded areas.");
    }
}
