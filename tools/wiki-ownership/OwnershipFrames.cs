// Documentation only: execute the production grid selectors against one always-ready
// schematic object per cell. No game, RPC, physics or installed-mod changes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using G = TooManyLags;
using P = TooManyLags.PlayerSimulation;

internal static class OwnershipFrames
{
    private const int Width = 50, Height = 50, Server = 99;
    private const float End = 300f;
    private sealed class Waypoint
    {
        internal float T, X, Z;
        internal Waypoint(float t, float x, float z) { T=t; X=x; Z=z; }
    }
    private static readonly Waypoint[][] Paths = BuildPaths();
    private static Waypoint W(float t,float x,float z) { return new Waypoint(t,x,z); }
    private static Waypoint[][] BuildPaths()
    {
        var anchors = new[] {
            new[] { W(3,10,37), W(9,10,37), W(20,7,43), W(38,18,42), W(58,16,31), W(76,7,29), W(91,9,37), W(112,20,29), W(125,24,25),
                W(129,23,26), W(134,25,27), W(139,26,25), W(144,24,23), W(149,22,24), W(154,24,26), W(159,26,26), W(164,25,24), W(169,23,25),
                W(190,16,32), W(202,6,31), W(219,7,40), W(233,17,39), W(244,15,34), W(258,10,35), W(268,10,37), W(300,10,37) },
            new[] { W(14,45,32), W(32,40,40), W(47,32,41), W(62,33,30), W(78,43,29), W(94,42,36), W(116,29,29), W(126,25,25),
                W(130,27,26), W(136,26,23), W(141,24,24), W(147,24,27), W(152,27,25), W(158,26,24), W(164,25,26), W(170,27,27),
                W(188,36,28), W(201,43,27), W(216,42,39), W(233,34,38), W(245,33,32), W(258,38,32), W(269,38,36), W(300,38,36) },
            new[] { W(28,32,5), W(48,40,12), W(64,39,20), W(81,28,20), W(99,27,10), W(117,25,22), W(127,25,24),
                W(132,23,24), W(137,24,22), W(143,26,23), W(148,25,25), W(153,23,26), W(158,24,24), W(164,26,24), W(170,25,23),
                W(188,31,15), W(204,41,14), W(217,40,9), W(238,28,9), W(251,27,18), W(268,35,18), W(285,32,12), W(300,32,12) }
        };
        var result = new Waypoint[anchors.Length][];
        for(int player=0;player<anchors.Length;player++)
        {
            // Fixed independent seeds: natural-looking detours, yet exactly
            // the same movement in every ownership-mode comparison.
            var random=new Random(733+(player+1)*2027);
            var path=new List<Waypoint>();path.Add(anchors[player][0]);
            for(int j=0;j<anchors[player].Length-1;j++)
            {
                Waypoint a=anchors[player][j],b=anchors[player][j+1];
                int steps=(a.X==b.X&&a.Z==b.Z)?1:Math.Max(1,(int)Math.Ceiling((b.T-a.T)/4));
                for(int k=1;k<steps;k++)
                {
                    float f=k/(float)steps;
                    float x=a.X+(b.X-a.X)*f+(float)(random.NextDouble()*1.6-.8);
                    float z=a.Z+(b.Z-a.Z)*f+(float)(random.NextDouble()*1.6-.8);
                    path.Add(W(a.T+(b.T-a.T)*f,Math.Max(4,Math.Min(45,x)),Math.Max(4,Math.Min(45,z))));
                }
                path.Add(b);
            }
            result[player]=path.ToArray();
        }
        return result;
    }
    private static int Index(int x,int z) { return z*Width+x; }
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    private static int[][][] Masks(object grid)
    {
        var all = (Array)grid.GetType().GetField("footprints",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(grid);
        var result = new int[all.Length][][];
        for(int i=0;i<all.Length;i++)
        {
            var offsets=(Array)all.GetValue(i); result[i]=new int[offsets.Length][];
            for(int j=0;j<offsets.Length;j++)
            {
                object offset=offsets.GetValue(j);
                object cell=offset.GetType().GetField("Cell",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(offset);
                result[i][j]=new[] {
                    (int)cell.GetType().GetField("X",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cell),
                    (int)cell.GetType().GetField("Z",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cell)
                };
            }
        }
        return result;
    }
    private static Dictionary<string,object> Position(int player,float t)
    {
        var path=Paths[player-1]; if(t<path[0].T) return null;
        float x=path[0].X,z=path[0].Z,yaw=0;
        for(int i=0;i<path.Length-1;i++)
        {
            var a=path[i];var b=path[i+1];
            if(t<a.T) break;
            float dx=b.X-a.X,dz=b.Z-a.Z;
            if(dx!=0||dz!=0) yaw=(float)(Math.Atan2(dx,dz)*180/Math.PI);
            if(t<=b.T)
            {
                float f=(t-a.T)/(b.T-a.T); x=a.X+dx*f;z=a.Z+dz*f;
                // Hold heading while working or waiting at the meeting point.
                break;
            }
            x=b.X;z=b.Z;
        }
        int cx=(int)Math.Floor(x+.5f),cz=(int)Math.Floor(z+.5f);
        int heading=G.AuthorityGrid.HeadingCoverage(yaw,30);
        return new Dictionary<string,object> { {"id",player},{"x",x},{"z",z},{"cx",cx},{"cz",cz},{"yaw",yaw},{"heading",heading} };
    }
    private static string Phase(float t)
    {
        if(t<3) return "Initial ownership";
        if(t<14) return "Player 1 is alone at the forge";
        if(t<28) return "Player 2 arrives from storage";
        if(t<120) return "Three players explore different parts of the base";
        if(t<173) return "Meeting: each player keeps moving differently";
        if(t<285) return "Players continue moving through their work areas";
        return "Later: forge, storage and farm";
    }
    private static float Ping(long uid) { return uid==1?80:uid==2?25:140; }
    private static Dictionary<string,object> Run(int mode)
    {
        bool keep=mode!=3, playerMode=mode==4;
        int initial=mode==1||playerMode?1:Server;
        var grid=new G.AuthorityGrid(Server,2,G.GridCoveragePattern.Rounded7x7,true,0,3,keep,true,.2f,1,!keep);
        var playerGrid=new P.AuthorityGrid(0,2,P.GridCoveragePattern.Rounded7x7,true,0,3,true,true,.2f,Ping,30);
        var masks=Masks(playerMode?(object)playerGrid:grid);
        int[] owners=new int[Width*Height];for(int j=0;j<owners.Length;j++)owners[j]=initial;
        var frames=new List<object>(); var peers=new List<G.GridPeer>();var playerPeers=new List<P.GridPeer>();
        int serverOverlapFrames=0, retainedFrames=0;
        for(int tick=0;tick<=(int)(End*20);tick++)
        {
            float now=tick/20f; peers.Clear();playerPeers.Clear();var people=new List<Dictionary<string,object>>();
            foreach(int id in new[]{1,2,3})
            {
                var pos=Position(id,now);if(pos==null)continue;people.Add(pos);
                int x=(int)pos["cx"],z=(int)pos["cz"],heading=(int)pos["heading"];
                peers.Add(G.GridPeer.Covered(id,new G.Cell(x,z),heading));
                playerPeers.Add(P.GridPeer.Covered(id,new P.Cell(x,z),heading));
            }
            if(playerMode)
            {
                playerGrid.Update(playerPeers,now);
                foreach(var cell in playerGrid.Changes)
                {
                    long owner=playerGrid.Owner(cell);if(owner==0)continue;
                    Check(owner>=1&&owner<=3,"Player mode selected a server or invalid owner.");
                    if(cell.X>=0&&cell.X<Width&&cell.Z>=0&&cell.Z<Height)owners[Index(cell.X,cell.Z)]=(int)owner;
                    playerGrid.Remember(cell,owner);
                }
            }
            else
            {
                grid.Update(peers,now);
                foreach(var cell in grid.Changes)
                {
                    long owner=grid.Owner(cell);if(owner==0&&keep)continue;
                    if(owner==0)owner=Server;
                    if(cell.X>=0&&cell.X<Width&&cell.Z>=0&&cell.Z<Height)owners[Index(cell.X,cell.Z)]=(int)owner;
                    grid.Remember(cell,owner);
                }
            }
            var cover=new int[Width*Height];
            foreach(var pos in people)
            {
                var cells=new List<int[]>();int cx=(int)pos["cx"],cz=(int)pos["cz"];
                foreach(int[] offset in masks[(int)pos["heading"]])
                {
                    int x=cx+offset[0],z=cz+offset[1];cells.Add(new[]{x,z});
                    if(x>=0&&x<Width&&z>=0&&z<Height)cover[Index(x,z)]++;
                }
                pos["coverage"]=cells;
            }
            bool serverOverlap=false,retained=false;
            for(int j=0;j<owners.Length;j++)
            {
                if(playerMode)Check(owners[j]!=Server,"NoServerSideSimulation created server ownership.");
                if(cover[j]>1&&owners[j]==Server)serverOverlap=true;
                if(cover[j]==0&&owners[j]!=Server&&owners[j]!=initial)retained=true;
                // Exit returns retain the production cell grace period. Assert
                // the settled result, not an invented immediate return.
                if(!keep&&cover[j]==0&&now>=296)Check(owners[j]==Server,"Settled vacated cell did not return to server.");
            }
            if(serverOverlap)serverOverlapFrames++;if(retained)retainedFrames++;
            if(tick%20==0)frames.Add(new Dictionary<string,object> { {"time",now},{"phase",Phase(now)},{"owners",(int[])owners.Clone()},{"players",people} });
        }
        Check(playerMode||serverOverlapFrames>0,"Scenario never demonstrated server overlap.");
        Check(!keep||retainedFrames>0,"Scenario never demonstrated retained ownership.");
        Check(owners[Index(10,37)]==1&&owners[Index(38,36)]==2&&owners[Index(32,12)]==3,"Final workplaces did not settle with their respective players.");
        return new Dictionary<string,object> { {"mode",mode},{"frames",frames},{"serverOverlapTicks",serverOverlapFrames},{"retainedTicks",retainedFrames} };
    }
    public static int Main(string[] args)
    {
        var modes=new List<object>();for(int mode=1;mode<=4;mode++)modes.Add(Run(mode));
        var serializer=new JavaScriptSerializer { MaxJsonLength=128*1024*1024 };
        File.WriteAllText(args[0],serializer.Serialize(new Dictionary<string,object> { {"width",Width},{"height",Height},{"modes",modes} }));
        Console.WriteLine("PASS: four matching five-minute, 50x50-cell scenarios use production simulation/player selectors; overlap, retention, return and final workplace assertions passed.");
        return 0;
    }
}
