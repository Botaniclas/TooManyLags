using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Mode=TooManyLags.PlayerSimulation;

// Real player-mode controller/index; handoff transport has its own production
// fixture. Here its source-consenting completion is deterministic.
namespace TooManyLags.PlayerSimulation
{
    internal enum Kinds { None=0,Signs=1,Portals=2,Incinerators=4,TamedAnimals=8,LooseItems=134217728 }
    internal sealed class Settings
    {
        internal float Interval=.05f,Full=2,Inner=0,Forward=.2f,Outer=3,CellSize=8,Margin=30;
        internal bool Keep=true,Directional=false,Mobs=false;
        internal GridCoveragePattern Pattern=GridCoveragePattern.Square3x3;
        internal Kinds Categories=Kinds.Signs|Kinds.Portals;
    }
    internal struct PlayerPeer { internal long Uid;internal ZDO Character;internal Vector3 Reference; }
    internal static class Plugin
    {
        internal static Network Network=new Network();internal static LocalAgent Local=null;internal static AuthorityController Controller=null;
        internal static void Fault(Exception error) { throw new Exception("Actual player grid failed",error); }
    }
    internal sealed class LocalAgent { internal void SetMobWatch(bool enabled) { } }
    internal sealed class Network
    {
        internal int RankVersion=0;
        internal bool Present(long uid) { return ZNet.instance.GetPeer(uid)!=null; }
        internal bool Capable(long uid) { return Present(uid); }
        internal bool SupportsLooseItems(long uid) { return Present(uid); }
        internal bool MobCapable(long uid) { return false; }
        internal float Latency(long uid) { return 25; }
        internal void FlushAll() { }
        internal void Players(List<PlayerPeer> target)
        { target.Clear();foreach(var peer in ZNet.instance.GetPeers()) { var actor=ZDOMan.instance.GetZDO(peer.m_characterID);if(actor!=null)target.Add(new PlayerPeer {Uid=peer.m_uid,Character=actor,Reference=peer.GetRefPos()}); } }
    }
    internal sealed class ObjectCatalog
    {
        private readonly Kinds enabled;internal ObjectCatalog(Kinds enabled) { this.enabled=enabled; }
        internal Kinds Prefab(int hash)
        { var root=ZNetScene.instance.GetPrefab(hash);if(!root)return Kinds.None;return root.GetComponent<Sign>()?(enabled&Kinds.Signs):root.GetComponent<TeleportWorld>()?(enabled&Kinds.Portals):Kinds.None; }
        internal bool NativeTransferSafe(int hash) { return true; }
        internal static bool OwnerRequest(Kinds kind,int method) { return true; }
    }
    internal sealed class Handoffs
    {
        internal int Requests;internal Handoffs(AuthorityController owner) { }
        internal bool Pending(ZDOID id) { return false; }
        internal bool Request(ZDO data,long target,Kinds kind,ZDOID mob) { Requests++;data.SetOwner(target);return true; }
        internal void Cancel(ZDOID id) { }internal void Observe(ZDO data) { }internal void Tick(float now) { }internal void Forget(ZDOID id) { }internal void Clear() { }
    }
    internal sealed class MobPolicy
    {
        internal MobPolicy(AuthorityController owner) { }internal void Changed(ZDO data) { }internal void RanksChanged() { }internal void Tick(float now) { }internal void Clear() { }
        internal void Forget(ZDOID id) { }internal void Wake(ZDOID id) { }internal bool Managed(ZDOID id,int method) { return false; }
        internal long Desired(ZDO data,ZDOID target) { return 0; }internal static bool FriendlyPrefab(int hash) { return false; }
    }
    internal static class RequestRouter { internal static void Flush(float now) { } }
}

internal static partial class IntegrationTests
{
    private static void TextEditingPlayerGrid()
    {
        foreach(Type type in new[]{typeof(Sign),typeof(TeleportWorld)})
        {
            Fresh(TooManyLags.Kinds.Signs|TooManyLags.Kinds.Portals,nativeSolo:true);
            var first=Peer(1);Peer(2);var data=Object(type,1,1,1);
            var mode=new Mode.AuthorityController(new Mode.Settings());
            Mode.Plugin.Controller=mode;
            Action<float> poll=delegate(float t){Time.unscaledTime=t;mode.Poll(t);Check("actual player controller remains active",mode.Active);};
            Func<int> count=delegate {return ((IDictionary)typeof(Mode.AuthorityController).GetField("nodes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(mode)).Count;};
            poll(0);poll(2.1f);
            try
            {
                Mode.AuthorityController.TextEditTarget=delegate(ZDO value){return ReferenceEquals(value,data)?2:0;};
                Check("player-mode prepares text through its actual object index",mode.PrepareTextEdit(data));poll(2.2f);poll(2.3f);
                Check("player-mode explicit editor replaces ordinary latency winner",data.GetOwner()==2);
                Mode.AuthorityController.TextEditTarget=null;Mode.AuthorityController.TextEditLocked=delegate(ZDO value){return ReferenceEquals(value,data);};mode.EndTextEdit(data);
                int requests=mode.Handoffs.Requests;
                MovePeer(first,1000);poll(3);poll(5);
                Check("losing shared-region contention does not cancel an active text editor",count()==1&&data.GetOwner()==2&&mode.Handoffs.Requests==requests);
                mode.Changed(data);poll(5.1f);
                Check("state replication retains held text node outside shared region",count()==1&&mode.Handoffs.Requests==requests);
                Mode.AuthorityController.TextEditLocked=null;mode.EndTextEdit(data);
                Check("ending edit outside shared coverage retires its extra node without a transfer",count()==0&&data.GetOwner()==2&&mode.Handoffs.Requests==requests);
                MovePeer(first,0);poll(6);poll(8.1f);poll(8.2f);
                Check("fresh shared-region entry resumes ordinary player-mode ownership",data.GetOwner()==1&&mode.Handoffs.Requests>requests);
                Mode.AuthorityController.TextEditTarget=delegate(ZDO value){return ReferenceEquals(value,data)?2:0;};mode.PrepareTextEdit(data);poll(9);poll(9.1f);
                Mode.AuthorityController.TextEditTarget=null;Mode.AuthorityController.TextEditLocked=delegate(ZDO value){return ReferenceEquals(value,data);};mode.EndTextEdit(data);
                Mode.AuthorityController.TextEditLocked=null;mode.EndTextEdit(data);mode.Wake(data.m_uid);poll(10);
                Check("late receipt cannot replay a discarded player-mode text takeover",data.GetOwner()==2);
                MovePeer(first,8);poll(11);poll(13.1f);poll(13.2f);
                Check("new real coverage can revisit the released player-mode text node",data.GetOwner()==1);
            }
            finally {Mode.AuthorityController.TextEditTarget=null;Mode.AuthorityController.TextEditLocked=null;mode.Stop();Mode.Plugin.Controller=null;}
        }
    }
}
