using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using New = TooManyLags;
using Old = AuthorityBaseline;

namespace AuthorityBaseline
{
    internal static class Plugin
    {
        internal const string Id = "botanic.toomanylags.fgnaddon", FgnId = "com.Fire.FiresGhettoNetworkMod";
        internal static AuthorityController Controller;
    }
}
internal static class RegionControllerBenchmark
{
    private struct Result { internal double Ms; internal int Fields, Rotations, Positions, Pairs; }
    private static Result Run(bool current, int count, string mode, bool defaults, int frames)
    {
        ZNet.instance=new ZNet();ZDOMan.instance=new ZDOMan();ZNetScene.instance=new ZNetScene();ZoneSystem.instance=new ZoneSystem();ZRoutedRpc.instance=new ZRoutedRpc();ZNet.IsSinglePlayer=false;
        FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority.Value=true;
        FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership.Value=false;
        FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnershipSelective.Value=false;
        FiresGhettoNetworkMod.StationRouter.ConfigEnabled.Value=true;FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive=true;FiresGhettoNetworkMod.FgnVersionGate.Match=false;
        var fgn=new FiresGhettoNetworkMod.FiresGhettoNetworkMod();Action tick;Action reset;Func<int> updates,pairs;
        Exception failure=null;
        if(current)
        {
            var controller=new New.AuthorityController(New.Kinds.Doors,2,new New.CompatibilityBridge(fgn),delegate(Exception e){failure=e;},false,delegate(Exception e){failure=e;},.25f,defaults,false,4,New.GridCoveragePattern.Rounded7x7,true,true);
            New.Plugin.Controller=controller;tick=controller.Tick;reset=controller.Reset;updates=delegate{return controller.FieldUpdates;};pairs=delegate{return controller.RegionGate.PairTests;};
        }
        else
        {
            var controller=new Old.AuthorityController(Old.Kinds.Doors,2,new Old.CompatibilityBridge(fgn),delegate(Exception e){failure=e;},false,delegate(Exception e){failure=e;},.25f,defaults,false,4,Old.GridCoveragePattern.Rounded7x7,true,true);
            Old.Plugin.Controller=controller;tick=controller.Tick;reset=controller.Reset;
            updates=delegate{return ((Old.AuthorityWorld)typeof(Old.AuthorityController).GetField("indexed",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller)).Grid.PlayerUpdates;};pairs=delegate{return 0;};
        }
        var characters=new List<ZDO>();
        for(int i=0;i<count;i++)
        {
            var data=new ZDO{m_uid=new ZDOID{ID=i+1},PersistentValue=false};characters.Add(data);ZDOMan.instance.Objects.Add(data);
            ZNet.instance.Peers.Add(new ZNetPeer{Uid=i+1,m_characterID=data.m_uid});
        }
        const int warm=256;
        for(int frame=0;frame<warm;frame++){Pose(characters,frame,mode);Time.time=Time.unscaledTime=frame*.25f;tick();}
        int fields=updates(),pairCount=pairs(),rotations=0,positions=0;
        foreach(var data in characters){rotations+=data.RotationReads;positions+=data.PositionReads;}
        var timer=Stopwatch.StartNew();
        for(int frame=warm;frame<frames+warm;frame++){Pose(characters,frame,mode);Time.time=Time.unscaledTime=frame*.25f;tick();}
        timer.Stop();if(failure!=null)throw failure;
        int finalRotations=0,finalPositions=0;foreach(var data in characters){finalRotations+=data.RotationReads;finalPositions+=data.PositionReads;}
        var result=new Result{Ms=timer.Elapsed.TotalMilliseconds,Fields=updates()-fields,Pairs=pairs()-pairCount,Rotations=finalRotations-rotations,Positions=finalPositions-positions};reset();return result;
    }
    private static void Pose(List<ZDO> data,int frame,string mode)
    {
        bool shared=mode.StartsWith("shared"),moving=mode.EndsWith("moving");
        for(int i=0;i<data.Count;i++)
        {
            float x=512+(shared?0:i*768)+(moving?(frame/2%32)*4:0);
            data[i].Position=new Vector3(x,0,0);data[i].Yaw=moving?frame%8*45:0;ZNet.instance.Peers[i].Position=data[i].Position;
        }
    }
    private static void Case(string mode,int count,bool defaults)
    {
        const int frames=20000;var oldTimes=new double[5];var newTimes=new double[5];Result a=default(Result),b=default(Result);
        for(int trial=0;trial<5;trial++)
        {
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            if((trial&1)==0){a=Run(false,count,mode,defaults,frames);b=Run(true,count,mode,defaults,frames);}
            else{b=Run(true,count,mode,defaults,frames);a=Run(false,count,mode,defaults,frames);}
            oldTimes[trial]=a.Ms;newTimes[trial]=b.Ms;
        }
        Array.Sort(oldTimes);Array.Sort(newTimes);
        if(defaults||mode.StartsWith("shared")){if(a.Fields!=b.Fields||a.Rotations!=b.Rotations)throw new Exception("Changed enabled raster behavior");}
        else if(b.Fields!=0||b.Rotations!=0)throw new Exception("Isolated players still do fine work");
        Console.WriteLine("mode="+mode+"; players="+count+"; server_default="+defaults+"; ticks="+frames+
            "; old_ms="+oldTimes[2].ToString("F3",CultureInfo.InvariantCulture)+"; new_ms="+newTimes[2].ToString("F3",CultureInfo.InvariantCulture)+
            "; old_fields="+a.Fields+"; new_fields="+b.Fields+"; old_rotations="+a.Rotations+"; new_rotations="+b.Rotations+"; old_positions="+a.Positions+"; new_positions="+b.Positions+"; new_pairs="+b.Pairs);
    }
    public static void Main()
    {
        try
        {
            Console.WriteLine("Synthetic full production-controller comparison against 3.6.0; Unity/network fixtures, no objects, five alternating trials, medians. Time includes pose fixture preparation; not real server timing.");
            Case("isolated_standing",1,false);Case("isolated_moving",1,false);Case("isolated_standing",12,false);Case("isolated_moving",12,false);
            Case("shared_standing",12,false);Case("shared_moving",12,false);Case("shared_standing",12,true);
        }
        catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
    }
}
