using System;
using System.Collections.Generic;
using System.Reflection;
#if PLAYER_SIMULATION
using TooManyLags.PlayerSimulation;
#else
using TooManyLags;
#endif

internal static class LegacyGridSchedulingTests
{
    private static int checks;
    private sealed class Node : AuthorityNode
    {
        internal long Owner = 7;
        internal int Calls;
        internal bool Ready = true;
        internal override long CurrentOwner { get { return Owner; } }
        internal override ApplyResult Apply(long target)
        {
            Calls++;
            if (!Ready) return ApplyResult.Pending;
            if (target != 0) { Owner = target; ManagedOwner = target; Managed = true; }
            return ApplyResult.Done;
        }
    }
    private static void Check(bool valid, string message)
    { checks++; if (!valid) throw new Exception(message); }
    private static List<GridPeer> Peers(int x)
    { return new List<GridPeer> { new GridPeer(1,new Cell(x,0)) }; }
    public static int Main()
    {
        try
        {
            Check(typeof(AuthorityWorld).GetField("nextWork",BindingFlags.Instance|BindingFlags.NonPublic)==null,
                "comparison has no future-only sleep gate");
            foreach (bool keep in new[]{false,true})
            {
                var world = new AuthorityWorld(99,2,GridCoveragePattern.Rounded7x7,keep,true,0,3,true,.2f);
#if PLAYER_SIMULATION
                world.AfterGridUpdate = null;
#endif
                var peers = Peers(0);
                var node = new Node { Cell=new Cell(2,0) }; world.Add(node,0);
                world.Tick(peers,0); world.Tick(peers,3);
                Check(node.Owner==1,"original outer field completes");
                world.Changed(node,4); int calls=node.Calls;
                for(int i=0;i<59;i++)world.Tick(peers,4+i*.05f);
                Check(node.Calls==calls,"original job barrier still respected");
                world.Tick(peers,7);
                Check(node.Calls==calls+1&&world.PendingCount==0,"due job completes at original deadline");
                world.Changed(node,8); node.NotBefore=Math.Min(node.NotBefore,8.1f); world.Tick(peers,8.1f);
                Check(node.Calls==calls+2,"original direct readiness advance is immediate");
                node.Ready=false; world.Changed(node,9); world.Tick(peers,12); calls=node.Calls;
                for(int i=1;i<=50;i++)world.Tick(peers,12+i*.05f);
                Check(node.Calls==calls+50,"due guarded jobs keep every original retry");
                node.Ready=true; world.Tick(peers,14.6f);
                Check(world.PendingCount==0,"guard completion releases work");
                world.Remove(node);world.Tick(new List<GridPeer>(),18);
                Check(world.PendingCount==0&&world.BucketCount==0,"unload clears object work");

                var grid = new AuthorityGrid(99,2,GridCoveragePattern.Rounded7x7,true,0,3,keep,true,.2f,fullRear:!keep);
                for(int i=0;i<2000;i++)grid.Update(Peers(i*20),i*4f);
                grid.Update(new List<GridPeer>(),8004);grid.Update(new List<GridPeer>(),8008);
                Check(grid.CachedCells==0&&grid.RememberedCells==0&&grid.DeferredCells==0,"travel retains no old world fields");
                Check(keep?grid.ReusablePolicies>0:grid.ReusablePolicies==0,"return-on-exit uses the original allocation rule");
                Check(grid.ReusablePolicies<=1024,"retention-mode pool remains bounded");
                Console.WriteLine("LEGACY GRID keep="+keep+": allocations="+grid.PolicyAllocations+"; pool="+grid.ReusablePolicies+".");
            }
            Console.WriteLine("PASS: "+checks+" restored scheduler, readiness, guard, pool and lifecycle checks.");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
