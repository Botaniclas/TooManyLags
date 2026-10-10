using System;
using System.Collections.Generic;
using TooManyLags;
internal static class GridTests
{
    private static int checks;
    private static void Check(string label,bool value){checks++;if(!value)throw new Exception(label);}
    private sealed class Node:AuthorityNode
    {
        internal long Owner; internal int Writes,Calls; internal bool Ready=true,Alive=true;
        internal Action OnApply;
        internal Node(Cell cell,long owner){Cell=cell;Owner=owner;}
        internal override long CurrentOwner{get{return Owner;}}
        internal override ApplyResult Apply(long target)
        {
            Calls++;if(OnApply!=null)OnApply();if(!Alive)return ApplyResult.Dropped;
            if(target==0)return ApplyResult.Done;if(!Ready)return ApplyResult.Pending;
            if(Owner!=target){Owner=target;Writes++;}Managed=true;ManagedOwner=Owner;return ApplyResult.Done;
        }
    }
    private static List<GridPeer> Peers(params GridPeer[] peers){return new List<GridPeer>(peers);}
    private static GridPeer P(long uid,int x,int z){return new GridPeer(uid,new Cell(x,z));}
    private static void Geometry()
    {
        foreach(int size in new[]{4,8,16})
        {
            Cell cell;
            Check("origin uses selected cell size",Cell.At(0,0,size,out cell)&&cell.Equals(new Cell(32/size,32/size)));
            Check("native sector alignment remains exact",Cell.At(32,32,size,out cell)&&cell.Equals(new Cell(64/size,64/size)));
            Check("negative edge uses floor",Cell.At(-32.01f,-32.01f,size,out cell)&&cell.Equals(new Cell(-1,-1)));
            Check("physical lower domain preserved",Cell.At(-16416,-16416,size,out cell)&&cell.Equals(new Cell(-16384/size,-16384/size)));
            Check("physical upper domain preserved",Cell.At(16351,16351,size,out cell)&&cell.Equals(new Cell(16384/size-1,16384/size-1)));
            Check("exclusive upper domain rejected",!Cell.At(16352,0,size,out cell));
            Check("outside lower domain rejected",!Cell.At(-16417,0,size,out cell));
            for(int i=-4;i<=4;i++)
            {
                float border=i*size-32;Check("border and predecessor are distinct fields",Cell.At(border-.01f,0,size,out cell)&&cell.X==i-1&&Cell.At(border,0,size,out cell)&&cell.X==i);
            }
        }
        Cell invalid;foreach(int size in new[]{0,2,12,32,-8})Check("unsupported cell sizes rejected",!Cell.At(0,0,size,out invalid));
    }
    private static bool RoundContains(int x,int z,int radius)
    {
        int[] widths=radius==3?new[]{3,3,2,1}:new[]{2,2,1};
        return Math.Abs(z)<=radius&&Math.Abs(x)<=widths[Math.Abs(z)];
    }
    private static void RoundedFields()
    {
        foreach(int radius in new[]{2,3})
        {
            GridCoveragePattern shape=radius==3?GridCoveragePattern.Rounded7x7:GridCoveragePattern.Rounded5x5;
            int count=radius==3?37:21;
            var grid=new AuthorityGrid(99,2,shape);grid.Update(Peers(P(1,0,0)),0);grid.Update(Peers(P(1,0,0)),2);
            Check("rounded footprint has exact field count",grid.Changes.Count==count&&grid.NeighborReads==count);
            for(int x=-radius;x<=radius;x++)for(int z=-radius;z<=radius;z++)Check("rounded footprint matches independent row-width oracle",grid.Owner(new Cell(x,z))==(RoundContains(x,z,radius)?1:0));
            foreach(int offset in new[]{1,2,3,4,5,6,7,-6})
            {
                grid=new AuthorityGrid(99,2,shape);var peers=Peers(P(1,0,0),P(2,offset,1));grid.Update(peers,0);grid.Update(peers,2);
                for(int x=-9;x<=10;x++)for(int z=-4;z<=5;z++)
                {
                    bool a=RoundContains(x,z,radius),b=RoundContains(x-offset,z-1,radius);
                    Check("rounded server ownership equals intersection",grid.Owner(new Cell(x,z))==(a&&b?99:a?1:b?2:0));
                }
            }
            var world=new AuthorityWorld(99,2,shape);var nodes=new List<Node>();int width=2*radius+1;
            for(int i=0;i<10000;i++){var node=new Node(new Cell(i%width-radius,(i/width)%width-radius),7);nodes.Add(node);world.Add(node,0);}
            world.Tick(Peers(P(1,0,0)),0);world.Tick(Peers(P(1,0,0)),2);int attempts=world.Attempts,reads=world.Grid.NeighborReads;
            for(int i=0;i<1000;i++)world.Tick(Peers(P(1,0,0)),3+i);
            Check("rounded stable 10000-object workload does no repeated jobs",world.Attempts==attempts&&world.Grid.NeighborReads==reads&&world.PendingCount==0);
        }
    }
    private static void ExitJobs()
    {
        foreach(GridCoveragePattern shape in new[]{GridCoveragePattern.Square3x3,GridCoveragePattern.Rounded5x5,GridCoveragePattern.Rounded7x7})
        foreach(bool keep in new[]{false,true})
        {
            var world=new AuthorityWorld(99,2,shape,keep);var nodes=new List<Node>();
            for(int i=0;i<10000;i++){var node=new Node(new Cell(0,0),7);nodes.Add(node);world.Add(node,0);}
            world.Tick(Peers(P(1,0,0)),0);world.Tick(Peers(P(1,0,0)),2);int attempts=world.Attempts;
            world.Tick(Peers(),3);world.Tick(Peers(),5);
            Check("vacated field drops policy rather than retaining stale coverage",world.Grid.Owner(new Cell(0,0))==0&&world.Grid.Candidate(new Cell(0,0))==0);
            Check("keep-on-exit skips all 10000 return jobs",world.Attempts==attempts+(keep?0:10000)&&world.PendingCount==0);
            int emptyAttempts=world.Attempts;
            for(int i=6;i<1006;i++)world.Tick(Peers(),i);
            Check("empty coverage never resumes repeated object jobs",world.Attempts==emptyAttempts);
            foreach(var node in nodes)Check("departure keeps actual owner",node.Owner==1&&node.Writes==1);
            world.Tick(Peers(P(2,0,0)),1007);world.Tick(Peers(P(2,0,0)),1008.99f);
            Check("new entrant still waits configured delay",nodes[0].Owner==1);
            world.Tick(Peers(P(2,0,0)),1009);
            foreach(var node in nodes)Check("new entrant can acquire all formerly vacated objects",node.Owner==2&&node.Writes==2);
        }
    }
    public static void Main()
    {
        Geometry();RoundedFields();ExitJobs();
        Cell c;
        Check("origin alignment",Cell.At(0,0,out c)&&c.Equals(new Cell(4,4)));
        Check("native border",Cell.At(32,32,out c)&&c.Equals(new Cell(8,8)));
        Check("negative boundary",Cell.At(-32.01f,-32.01f,out c)&&c.Equals(new Cell(-1,-1)));
        Check("invalid position rejected",!Cell.At(float.NaN,0,out c)&&!Cell.At(float.PositiveInfinity,0,out c)&&!Cell.At(1000000,0,out c));
        // Keep a pair 12m apart while both walk across the same grid borders.
        var parallel=new AuthorityGrid(99,2);Cell observed;Cell.At(12,0,out observed);
        float[] walk={0,2,4,6,7,5,3,1};long before=0;int flips=0;
        for(int step=0;step<32;step++)
        {
            float x=walk[step%walk.Length];Cell left,right;Cell.At(x,0,out left);Cell.At(x+12,0,out right);
            parallel.Update(Peers(new GridPeer(1,left),new GridPeer(2,right)),step*2);
            long owner=parallel.Owner(observed);if(owner!=0&&before!=0&&owner!=before)flips++;if(owner!=0)before=owner;
        }
        Check("constant 12m pair cannot flap at cell boundaries",flips==0&&before==99);
        // Independent rectangle-membership oracle (not the runtime dictionary algorithm).
        int[,] offsets={{1,0},{1,1},{2,0},{2,2},{3,0},{-1,0},{-1,-1},{-2,-2}};
        for(int test=0;test<offsets.GetLength(0);test++)
        {
            int dx=offsets[test,0],dz=offsets[test,1];var overlap=new AuthorityGrid(99,2);
            var pair=Peers(P(1,0,0),P(2,dx,dz));overlap.Update(pair,0);overlap.Update(pair,2);
            for(int x=-4;x<=4;x++)for(int z=-4;z<=4;z++)
            {
                bool inA=Math.Abs(x)<=1&&Math.Abs(z)<=1,inB=Math.Abs(x-dx)<=1&&Math.Abs(z-dz)<=1;
                long expected=inA&&inB?99:inA?1:inB?2:0;
                Check("only shared cells choose server at offset "+dx+","+dz+" field "+x+","+z,overlap.Owner(new Cell(x,z))==expected);
            }
        }
        var triple=new AuthorityGrid(99,2);var three=Peers(P(1,0,0),P(2,2,0),P(3,0,2));triple.Update(three,0);triple.Update(three,2);
        Check("three-player overlap does not spread server across entire blocks",triple.Owner(new Cell(0,0))==1&&triple.Owner(new Cell(2,0))==2&&triple.Owner(new Cell(0,2))==3&&triple.Owner(new Cell(1,1))==99);
        int coverageReads=triple.NeighborReads;for(int i=0;i<1000;i++)triple.Update(three,3+i);
        Check("stable coverage has no repeated cell lookups",triple.NeighborReads==coverageReads);
        var walkingPair=new AuthorityGrid(99,2);walkingPair.Update(Peers(P(1,0,0),P(2,1,0)),0);
        walkingPair.Update(Peers(P(1,1,0),P(2,2,0)),2);
        Check("new overlap inherits the still-pending first player's timer",walkingPair.Owner(new Cell(1,0))==99&&walkingPair.Owner(new Cell(2,0))==99);
        walkingPair.Update(Peers(P(1,1,0),P(2,2,0)),4);
        Check("new shared row matures and single-player front stays client",walkingPair.Owner(new Cell(2,0))==99&&walkingPair.Owner(new Cell(3,0))==2);
        walkingPair.Update(Peers(P(1,1,0)),5);walkingPair.Update(Peers(P(1,1,0)),6.99f);
        Check("shared departure keeps configured grace",walkingPair.Owner(new Cell(1,0))==99);
        walkingPair.Update(Peers(P(1,1,0)),7);Check("remaining visitor takes former overlap after delay",walkingPair.Owner(new Cell(1,0))==1);
        var movingGrid=new AuthorityGrid(99,2);movingGrid.Update(Peers(P(1,0,0),P(2,0,0)),0);
        movingGrid.Update(Peers(P(1,1,0),P(2,1,0)),2);
        int readyFields=0;for(int x=0;x<=1;x++)for(int z=-1;z<=1;z++)if(movingGrid.Owner(new Cell(x,z))==99)readyFields++;
        Check("six overlapping fields settle even as both players cross a cell",readyFields==6);
        Check("new front field must wait residence",movingGrid.Owner(new Cell(2,0))==0);
        movingGrid.Update(Peers(P(1,2,0),P(2,2,0)),4);
        readyFields=0;for(int x=1;x<=2;x++)for(int z=-1;z<=1;z++)if(movingGrid.Owner(new Cell(x,z))==99)readyFields++;
        Check("continued walking never resets unchanged overlap timers",readyFields==6);
        movingGrid.Update(Peers(P(1,2,0),P(2,2,0)),6);
        for(int x=1;x<=3;x++)for(int z=-1;z<=1;z++)Check("all nine mature fields use server",movingGrid.Owner(new Cell(x,z))==99);
        var a=P(1,0,0);var b=P(2,1,0);var grid=new AuthorityGrid(99,4);
        var shortDelay=new AuthorityGrid(99,2);
        shortDelay.Update(Peers(a),0);shortDelay.Update(Peers(a),1.99f);
        Check("two-second solo delay not early",shortDelay.Owner(a.Cell)==0);
        shortDelay.Update(Peers(a),2);Check("two-second solo assignment",shortDelay.Owner(a.Cell)==1);
        shortDelay.Update(Peers(a,b),3);shortDelay.Update(Peers(a,b),4.99f);
        Check("two-second nearby pair delay not early",shortDelay.Owner(a.Cell)==1);
        shortDelay.Update(Peers(a,b),5);Check("stable nearby pair transfers after two seconds",shortDelay.Owner(a.Cell)==99);
        shortDelay.Update(Peers(a),6);shortDelay.Update(Peers(a,b),7);shortDelay.Update(Peers(a,b),9);
        Check("short departure retains server owner",shortDelay.Owner(a.Cell)==99);
        shortDelay.Update(Peers(a),10);shortDelay.Update(Peers(a),11.99f);
        Check("departure also observes configured delay",shortDelay.Owner(a.Cell)==99);
        shortDelay.Update(Peers(a),12);Check("stable remaining player takes field",shortDelay.Owner(a.Cell)==1);
        var customDelay=new AuthorityGrid(99,5.5f);customDelay.Update(Peers(a,b),0);customDelay.Update(Peers(a,b),5.49f);
        Check("longer custom wait remains configurable",customDelay.Owner(a.Cell)==0);
        customDelay.Update(Peers(a,b),5.5f);Check("fractional wait reaches configured threshold",customDelay.Owner(a.Cell)==99);
        var delayedWorld=new AuthorityWorld(99,2);var guardedNode=new Node(a.Cell,1){Ready=false};delayedWorld.Add(guardedNode,0);
        delayedWorld.Tick(Peers(a,b),0);delayedWorld.Tick(Peers(a,b),2);
        Check("two-second wait never overrides pending readiness",guardedNode.Owner==1&&guardedNode.Writes==0&&delayedWorld.PendingCount==1);
        guardedNode.Ready=true;delayedWorld.Tick(Peers(a,b),4);
        Check("ready guard permits pending transfer on next native cycle",guardedNode.Owner==99&&guardedNode.Writes==1&&delayedWorld.PendingCount==0);
        grid.Update(Peers(a),0);Check("delay before assignment",grid.Owner(a.Cell)==0);
        grid.Update(Peers(a),3.9f);Check("delay respected",grid.Owner(a.Cell)==0);
        grid.Update(Peers(a),4);Check("single player gets full block",grid.Owner(a.Cell)==1&&grid.Changes.Count==9);
        grid.Update(Peers(a,b),5);Check("second player not immediate",grid.Owner(a.Cell)==1);
        grid.Update(Peers(a,b),9);Check("near pair assigns only intersection to server",grid.Owner(a.Cell)==99&&grid.Owner(new Cell(-1,1))==1&&grid.Owner(new Cell(2,1))==2&&grid.Changes.Count==9);
        grid.Update(Peers(a),10);grid.Update(Peers(a,b),12);grid.Update(Peers(a,b),16);
        Check("short leave reentry does not flap",grid.Owner(a.Cell)==99);
        grid.Update(Peers(a),17);grid.Update(Peers(a),21);Check("remaining player takes area",grid.Owner(a.Cell)==1);
        grid.Update(Peers(),22);grid.Update(Peers(),26);Check("empty grid forgets policy",grid.Owner(a.Cell)==0&&grid.Candidate(a.Cell)==0);
        grid=new AuthorityGrid(99,4);grid.Update(Peers(a,P(2,0,0)),0);grid.Update(Peers(a,P(2,0,0)),4);
        Check("same cell counts distinct players",grid.Owner(a.Cell)==99&&grid.Changes.Count==9);
        grid=new AuthorityGrid(99,4);grid.Update(Peers(a,P(2,4,0)),0);grid.Update(Peers(a,P(2,4,0)),4);
        Check("far players both own areas",grid.Owner(a.Cell)==1&&grid.Owner(new Cell(4,0))==2);
        grid=new AuthorityGrid(99,4);grid.Update(Peers(a,P(2,2,0)),0);grid.Update(Peers(a,P(2,2,0)),4);
        Check("two-cell separation still gives real intersection to server",grid.Owner(new Cell(1,0))==99&&grid.Owner(new Cell(0,0))==1&&grid.Owner(new Cell(2,0))==2);
        grid=new AuthorityGrid(99,4);grid.Update(Peers(P(2,2,0)),0);grid.Update(Peers(P(2,2,0)),4);
        grid.Update(Peers(a,P(2,2,0)),5);grid.Update(Peers(a,P(2,2,0)),9);
        Check("new covering player makes only shared existing cell server-owned",grid.Owner(new Cell(1,0))==99&&grid.Owner(new Cell(2,0))==2);
        int builds=grid.Rebuilds,reads=grid.NeighborReads;for(int i=0;i<1000;i++)grid.Update(Peers(a,P(2,2,0)),10+i);
        Check("stable grid does not rerun neighbors",grid.Rebuilds==builds&&grid.NeighborReads==reads);
        var world=new AuthorityWorld(99,4);var node=new Node(a.Cell,2);world.Add(node,0);
        world.Tick(Peers(a),0);world.Tick(Peers(a),4);Check("foreign player owner can be taken",node.Owner==1&&node.Writes==1);
        world.Tick(Peers(),5);world.Tick(Peers(),9);Check("leaving preserves last owner",node.Owner==1&&node.Writes==1);
        world.Tick(Peers(P(2,0,0)),10);world.Tick(Peers(P(2,0,0)),14);Check("new visit takes sticky owner",node.Owner==2&&node.Writes==2);
        world.Move(node,new Cell(1,0),15);world.Tick(Peers(P(2,0,0),P(3,2,0)),15);
        world.Tick(Peers(P(2,0,0),P(3,2,0)),18);Check("mobile residence delay",node.Writes==2);
        node.Ready=false;world.Tick(Peers(P(2,0,0),P(3,2,0)),19);Check("only awaiting guard remains scheduled",world.PendingCount==1);
        node.Ready=true;world.Tick(Peers(P(2,0,0),P(3,2,0)),21);Check("guard recovery resumes",world.PendingCount==0);
        node.Owner=17;world.Changed(node,22);world.Tick(Peers(P(2,0,0),P(3,2,0)),25);
        Check("external claim gets grace",node.Owner==17);world.Tick(Peers(P(2,0,0),P(3,2,0)),26);Check("external claim reconciled",node.Owner!=17);
        world.Changed(node,100);world.Tick(Peers(P(2,0,0),P(3,2,0)),10);world.Tick(Peers(P(2,0,0),P(3,2,0)),14);
        Check("clock rollback does not stall work",world.PendingCount==0);
        world.Remove(node);Check("empty bucket released",world.BucketCount==0&&world.PendingCount==0);
        var bulk=new List<Node>();world=new AuthorityWorld(99,4);
        for(int i=0;i<10000;i++){var n=new Node(a.Cell,2);world.Add(n,0);bulk.Add(n);}
        world.Tick(Peers(a),0);world.Tick(Peers(a),4);int attempts=world.Attempts;
        for(int i=0;i<1000;i++)world.Tick(Peers(a),5+i);
        Check("10000 stable objects get no repeated jobs",world.Attempts==attempts&&world.PendingCount==0);
        Node other=new Node(new Cell(50,50),7);world.Add(other,1100);world.Tick(Peers(a),1100);int otherCalls=other.Calls;
        world.Tick(Peers(a,b),1101);world.Tick(Peers(a,b),1105);
        Check("only affected buckets visited",other.Calls==otherCalls);
        foreach(var n in bulk)world.Remove(n);world.Remove(other);Check("all loaded index refs cleaned",world.BucketCount==0&&world.PendingCount==0);
        world=new AuthorityWorld(99,4);node=new Node(a.Cell,2);world.Add(node,0);node.OnApply=delegate{world.Remove(node);};world.Tick(Peers(a),0);
        Check("removal during handler safe",world.PendingCount==0&&world.BucketCount==0);
        Console.WriteLine("PASS: "+checks+" grid/index assertions; stable 10000-object/1000-cycle test produces zero repeated object jobs.");
    }
}
