using System;
using System.Reflection;
using TooManyLags;
using UnityEngine;

internal static partial class IntegrationTests
{
    private static void StaticGridCoverage()
    {
        int start=checks;
        Fresh(Kinds.Doors,statics:true,nativeSolo:true);Peer(1);
        var wall=Object(null,1,1,99);Tick(0);Tick(3);
        Check("static grid is opt-in; server-only statics stay on server",wall.GetOwner()==99&&controller.RequestNode(wall.m_uid,"RPC_Repair".GetStableHashCode())==null);
        foreach(bool keep in new[]{true,false})
        {
            Fresh(Kinds.Doors,statics:true,staticGrid:true,nativeSolo:true,keepOnExit:keep);
            var first=Peer(1);wall=Object(null,1,1,99);var outside=Object(null,100,1,99);Tick(0);Tick(2.1f);
            Check("a solo player receives nearby static grid objects without PUP",wall.GetOwner()==1&&outside.GetOwner()==99);
            MovePeer(first,48);Tick(3);Tick(6);
            Check("static grid exit follows selected CPU retention policy",wall.GetOwner()==(keep?1:99));
        }
        Fresh(Kinds.Doors,statics:true,staticGrid:true,nativeSolo:true);
        var a=Peer(1);var b=Peer(2,48);var left=Object(null,1,1,99);var right=Object(null,49,1,99);var middle=Object(null,25,1,99);Tick(0);Tick(2.1f);
        Check("separated players share static simulation in one native region",left.GetOwner()==1&&right.GetOwner()==2&&middle.GetOwner()==99);
        MovePeer(b,0);Tick(3);Tick(6);
        Check("overlapping static fields use server ownership",left.GetOwner()==99);
        MovePeer(a,48);Tick(7);Tick(10);
        Check("a remaining player can receive a static field after overlap",left.GetOwner()==2);
        int attempts=controller.ScheduledAttempts,scans=GameObject.Scans,lookups=GameObject.Lookups;
        for(int i=0;i<1000;i++)Tick(11+i*.05f);
        Check("settled static grid adds no idle object jobs or hierarchy work",attempts==controller.ScheduledAttempts&&scans==GameObject.Scans&&lookups==GameObject.Lookups);

        Fresh(Kinds.Doors,defaultInteractives:true,staticGrid:true,nativeSolo:true);Peer(1);
        wall=Object(null,1,1,7);var far=Object(null,100,1,7);var door=Object(typeof(Door),100,2,99);Tick(0);Tick(3);
        Check("static and interactive server defaults are independent",wall.GetOwner()==1&&far.GetOwner()==7&&door.GetOwner()==99);
        Check("interactive default does not rewrite a native static assignment",controller.NativeOwnerTarget(far,7)==7);

        Fresh(Kinds.Doors,statics:true,staticGrid:true,nativeSolo:true);Peer(1);
        var baked=Object(null);baked.Parked=true;var physics=Object(null);ZNetScene.instance.FindInstance(physics).gameObject.Add(new Rigidbody());
        var actor=Object(typeof(Character));Tick(0);Tick(3);
        Check("baked PUP originals remain outside static grid",baked.GetOwner()==7&&baked.Writes==0&&controller.Persistent(baked));
        Check("static grid excludes dynamic bodies and creatures",physics.GetOwner()==7&&actor.GetOwner()==7);
        baked.Parked=false;var restored=ZNetScene.instance.FindInstance(baked);controller.Register(baked,restored);Tick(4);Tick(7);
        Check("a restored ordinary piece can reenter the same static grid",baked.GetOwner()==1);
#if GRID_PROTOCOL
        StaticInitialProof();
#endif
        StaticWorldObjects();
        Console.WriteLine("PASS: "+(checks-start)+" optional static-grid assertions: per-type defaults, solo/shared regions, overlap, retention, no idle rescans, PUP exclusions/restoration and no PUP dependency.");
    }
    private static ZDO StaticWorld(Type type,float x=1,long owner=7,Action<GameObject> configure=null,bool register=true)
    {
        var root=new GameObject{name="world"+next};var view=root.Add(new ZNetView());
        root.Add((Component)Activator.CreateInstance(type));
        if(type==typeof(WearNTear))
        {
            root.name="vines";var visual=new GameObject();visual.transform.parent=root.transform;root.Children.Add(visual);visual.Add(new UnknownModScript());
        }
        if(configure!=null)configure(root);
        root.transform.position=new Vector3(x,0,1);
        var data=new ZDO{m_uid=new ZDOID{ID=next++},Hash=100+next,Position=root.transform.position};
        data.SetOwner(owner);data.Writes=0;view.Data=data;
        ZDOMan.instance.Objects.Add(data);ZNetScene.instance.Views.Add(data,view);ZNetScene.instance.Prefabs.Add(data.Hash,root);
        if(register)controller.Register(data,view);return data;
    }
    private static void StaticWorldObjects()
    {
        Fresh(Kinds.None,statics:true,staticGrid:true,nativeSolo:true);var peer=Peer(1);
        var wall=StaticWorld(typeof(Piece),configure:delegate(GameObject root){
            root.Add(new WearNTear());root.Add(new UnknownModScript());
            var visual=new GameObject();visual.transform.parent=root.transform;root.Children.Add(visual);visual.Add(new UnknownModScript());});
        Check("extra root and visual scripts do not exclude a fixed wall",ObjectCatalog.Classify(ZNetScene.instance.FindInstance(wall).gameObject)==Kinds.StaticBuildings);
        var distant=StaticWorld(typeof(TreeBase),configure:delegate(GameObject root){root.GetComponent<ZNetView>().m_distant=true;});
        var worldTypes=new[]{typeof(TreeBase),typeof(MineRock),typeof(MineRock5),typeof(Destructible),typeof(WearNTear)};
        var worlds=new System.Collections.Generic.List<ZDO>();
        foreach(var type in worldTypes){worlds.Add(StaticWorld(type));worlds.Add(StaticWorld(type,100));}
        Tick(0);Tick(3);
        Check("a wall with visual scripts participates in the grid",wall.GetOwner()==1);
        Check("native distant standing trees also participate in the grid",distant.GetOwner()==1);
        for(int i=0;i<worlds.Count;i++)Check("stationary world roots share the static settings",worlds[i].GetOwner()==(i%2==0?1:99));
        MovePeer(peer,48);Tick(4);Tick(7);
        foreach(var data in worlds)Check("retention keeps acquired static world roots",data.GetOwner()==(worlds.IndexOf(data)%2==0?1:99));
        int scans=GameObject.Scans,attempts=controller.ScheduledAttempts;
        for(int i=0;i<100;i++)Tick(8+i*.1f);
        Check("static world roots add no settled hierarchy scans or object jobs",scans==GameObject.Scans&&attempts==controller.ScheduledAttempts);

        Fresh(Kinds.None,statics:true,staticGrid:true,nativeSolo:true,keepOnExit:false);peer=Peer(1);
        var tree=StaticWorld(typeof(TreeBase));Tick(0);Tick(3);MovePeer(peer,48);Tick(4);Tick(7);
        Check("static world roots return to the server when exit retention is off",tree.GetOwner()==99);
        Fresh(Kinds.None,statics:true,staticGrid:true,nativeSolo:true);peer=Peer(1);
        var scenery=StaticWorld(typeof(TreeBase),200,0,configure:delegate(GameObject root){root.GetComponent<ZNetView>().m_distant=true;});
        ZNetScene.instance.Ready=delegate{return false;};Tick(0);Tick(3);attempts=controller.ScheduledAttempts;
        for(int i=4;i<20;i++)Tick(i);
        Check("unowned distant scenery outside simulation adds no repeated jobs",scenery.GetOwner()==0&&attempts==controller.ScheduledAttempts);
        MovePeer(peer,128);Tick(20);
        scenery.SetOwner(controller.NativeOwnerTarget(scenery,1));controller.OwnerChanged(scenery);Tick(21);
        Check("native area entry wakes distant scenery with its server default",scenery.GetOwner()==99);
        MovePeer(peer,200);ZNetScene.instance.Ready=delegate{return true;};Tick(22);Tick(25);
        Check("previously distant scenery joins a newly covering player grid",scenery.GetOwner()==1);
        Fresh(Kinds.None,statics:true,nativeSolo:true);Peer(1);
        foreach(var type in worldTypes)StaticWorld(type,configure:delegate(GameObject root){root.GetComponent<ZNetView>().m_distant=type==typeof(TreeBase);});
        Tick(0);Tick(3);
        foreach(var data in ZDOMan.instance.Objects)if(data.Persistent)
            Check("server-only statics also accept world roots without WearNTear",!controller.Persistent(data)&&data.GetOwner()==99);
        Fresh(Kinds.None,staticGrid:true,nativeSolo:true);Peer(1);
        tree=StaticWorld(typeof(TreeBase),100);Tick(0);Tick(3);
        Check("world roots retain vanilla defaults when static default is off",tree.GetOwner()==7&&controller.NativeOwnerTarget(tree,7)==7);

        Fresh(Kinds.None,statics:true,staticGrid:true,nativeSolo:true);peer=Peer(1);Tick(0);Tick(3);
        foreach(var type in worldTypes)
        {
            var data=StaticWorld(type,40,0,configure:delegate(GameObject root){root.GetComponent<ZNetView>().m_distant=true;},register:false);var live=ZNetScene.instance.FindInstance(data);ZNetScene.instance.Views.Remove(data);
            controller.InitialMobLoad(data);
            Check("unowned static world root gets server default before its Awake",data.GetOwner()==99&&data.Writes==1);
            controller.InitialMobLoad(data);Check("existing static owner is not overwritten at construction",data.Writes==1);
            ZNetScene.instance.Views.Add(data,live);
        }
        var active=StaticWorld(typeof(TreeBase),100,7,register:false);controller.InitialMobLoad(active);
        Check("static first load never steals an existing player owner",active.GetOwner()==7&&active.Writes==0);
        var red=StaticWorld(typeof(TreeBase),1,0,register:false);controller.InitialMobLoad(red);
        Check("first-load static seed respects the settled player field",red.GetOwner()==0&&controller.NativeOwnerTarget(red,1)==1);
        var baked=StaticWorld(typeof(TreeBase),100,0,register:false);baked.Parked=true;controller.InitialMobLoad(baked);
        Check("static first-load seed excludes PUP-baked data",baked.GetOwner()==0&&baked.Writes==0);

        Action<GameObject>[] unsafeRoots={
            delegate(GameObject root){root.Add(new Rigidbody());},
            delegate(GameObject root){root.Add(new ZSyncTransform());},
            delegate(GameObject root){root.Add(new TreeLog());},
            delegate(GameObject root){root.Add(new BaseAI());},
            delegate(GameObject root){root.Add(new Character());},
            delegate(GameObject root){root.Add(new Container());},
            delegate(GameObject root){root.Add(new SpawnArea());},
            delegate(GameObject root){root.Add(new CreatureSpawner());},
            delegate(GameObject root){root.Add(new SpawnSystem());},
            delegate(GameObject root){root.Add(new Destructible{m_ttl=10});},
            delegate(GameObject root){root.Add(new WearNTear{m_staticPosition=false});},
            delegate(GameObject root){root.GetComponent<ZNetView>().m_persistent=false;},
            delegate(GameObject root){var child=new GameObject();child.transform.parent=root.transform;root.Children.Add(child);child.Add(new ZNetView());},
            delegate(GameObject root){var child=new GameObject();child.transform.parent=root.transform;root.Children.Add(child);child.Add(new Door());}
        };
        foreach(var configure in unsafeRoots)
        {
            var data=StaticWorld(typeof(TreeBase),configure:configure,register:false);
            Check("unsafe or interactive world roots stay outside the static policy",!StaticBuildings.Safe(ZNetScene.instance.FindInstance(data).gameObject));
        }
        Check("MineRock hit requests follow the owner",ObjectCatalog.OwnerRequest(Kinds.StaticBuildings,"Hit".GetStableHashCode()));
        foreach(string name in new[]{"Hide","RPC_SetAreaHealth","RPC_Shake","RPC_Grow"})
            Check("static visual broadcasts are never redirected",!ObjectCatalog.OwnerRequest(Kinds.StaticBuildings,name.GetStableHashCode()));
        var looseVine=StaticWorld(typeof(WearNTear),configure:delegate(GameObject root){root.Add(new Rigidbody());},register:false);
        Check("no-Piece recognition still excludes physics",!StaticBuildings.Safe(ZNetScene.instance.FindInstance(looseVine).gameObject));
        var movingVine=StaticWorld(typeof(WearNTear),configure:delegate(GameObject root){root.GetComponent<WearNTear>().m_staticPosition=false;},register:false);
        Check("no-Piece recognition still requires fixed WearNTear",!StaticBuildings.Safe(ZNetScene.instance.FindInstance(movingVine).gameObject));
        var harvestVine=StaticWorld(typeof(WearNTear),configure:delegate(GameObject root){root.Add(new Pickable());},register:false);
        Check("harvestable vines keep their interactive category",ObjectCatalog.Classify(ZNetScene.instance.FindInstance(harvestVine).gameObject)==Kinds.Pickables);
        var unanchored=StaticWorld(typeof(Piece),register:false);
        Check("a build-menu Piece alone is no static safety anchor",ObjectCatalog.Classify(ZNetScene.instance.FindInstance(unanchored).gameObject)==Kinds.None);
    }
#if GRID_PROTOCOL
    private static void StaticInitialProof()
    {
        foreach(Type type in new[]{typeof(Piece),typeof(TreeBase)})
        {
            Fresh(Kinds.None,statics:true,staticGrid:true,nativeSolo:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
            var peer=Peer(1);GridStart();
            try
            {
                HarvestHello(peer);Tick(0);var sent=GridCapture(peer);
                var template=new GameObject{name="static_initial",activeInHierarchy=false};template.Add(new ZNetView{m_distant=type==typeof(TreeBase)});
                template.Add((Component)Activator.CreateInstance(type));if(type==typeof(Piece))template.Add(new WearNTear());
                var data=new ZDO{m_uid=new ZDOID{ID=next++},Hash=100+next,Position=new Vector3(1,0,1)};
                ZDOMan.instance.Objects.Add(data);ZNetScene.instance.Prefabs.Add(data.Hash,template);
                ZNetScene.instance.Ready=delegate{throw new Exception("Static assignment must not query broad readiness.");};
                Check("native first assignment still prefers settled grid",controller.NativeOwnerTarget(data,1)==1);
                data.SetOwner(99);controller.OwnerChanged(data);Tick(.1f);
                Check("uncreated static root receives grid owner without load proof",data.GetOwner()==1&&!data.Created&&sent.Count==0&&ZNetScene.instance.AreaQueries==0);
            }
            finally{TooManyLags.GridHandoffs.Protocol.Stop();}
        }
    }
#endif
}
