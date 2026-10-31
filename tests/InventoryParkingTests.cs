using System;
using TooManyLags;
using UnityEngine;

internal static partial class IntegrationTests
{
    private static void InventoryParkingAndCooldown()
    {
        foreach(bool retain in new[]{false,true})
        {
            Fresh(Kinds.Containers|Kinds.Doors,defaultInteractives:true,keepOnExit:retain,
                directional:true,checkIntervalSeconds:.05f);
            var player=Peer(1);Peer(7,64);
            var data=Object(typeof(Container));var view=ZNetScene.instance.FindInstance(data);
            var chest=view.GetComponent<Container>();var neighbor=Object(typeof(Door),1,1,1);
            int queries=0;bool grant=false;
            view.OnInvoke=delegate
            {
                queries++;
                if(grant){Import(data,99,false);controller.InventoryResponse(chest,7,true);}
                else controller.InventoryResponse(chest,7,false);
            };
            Tick(0);int attempts=controller.ScheduledAttempts;
            var node=controller.RequestNode(data.m_uid,"RPC_RequestOpen".GetStableHashCode());
            for(int i=1;i<=19;i++)
            {
                ZDOMan.instance.GetZDO(player.m_characterID).Yaw=(i%4)*90;
                Tick(i*.05f);
            }
            Check("one-second native refusal gate avoids repeated queries and object jobs",queries==1&&controller.ScheduledAttempts==attempts&&node.WaitingForRequest);
            Check("blocked inventory does not certify its partially owned field or rewrite its neighbor",!Index().Grid.IsRemembered(node.Cell,1)&&neighbor.Writes==0);
            Tick(1.01f);Check("refusal expiry alone does not retry",queries==1&&Index().PendingCount==0);
            ZDOMan.instance.GetZDO(player.m_characterID).Yaw=0;Tick(1.07f);
            Check("fresh coverage after one second starts exactly one new native query",queries==2&&node.WaitingForRequest);
            grant=true;ZDOMan.instance.GetZDO(player.m_characterID).Yaw=90;Tick(1.13f);
            Check("second refusal starts its own one-second gate",queries==2&&data.GetOwner()==7);
            ZDOMan.instance.GetZDO(player.m_characterID).Yaw=180;Tick(2.13f);Tick(2.2f);
            Check("fresh post-gate request grants and certifies the complete field",queries==3&&data.GetOwner()==1&&Index().Grid.IsRemembered(node.Cell,1)==retain);
            Check("successful request removes the sparse refusal listener",Index().Grid.Renewed==null);

            Fresh(Kinds.Containers,defaultInteractives:true,keepOnExit:retain,
                directional:true,checkIntervalSeconds:.05f);
            player=Peer(1);data=Object(typeof(Container),1,1,99);view=ZNetScene.instance.FindInstance(data);
            chest=view.GetComponent<Container>();chest.InUse=true;queries=0;view.OnInvoke=delegate{queries++;};
            Tick(0);attempts=controller.ScheduledAttempts;
            node=controller.RequestNode(data.m_uid,"RPC_RequestOpen".GetStableHashCode());
            for(int i=1;i<=100;i++)Tick(i*.05f);
            Check("local busy chest parks with no idle object work or remote query",node.WaitingForRequest&&controller.ScheduledAttempts==attempts&&queries==0&&data.GetOwner()==99);
            chest.InUse=false;Tick(5.1f);
            Check("local use completion alone cannot revive the discarded transfer",data.GetOwner()==99&&controller.ScheduledAttempts==attempts);
            ZDOMan.instance.GetZDO(player.m_characterID).Yaw=90;Tick(5.2f);
            Check("fresh coverage claims the idle local chest without source consent traffic",data.GetOwner()==1&&queries==0&&!node.WaitingForRequest);

            // A source may already have accepted while its state is in flight.
            // Local use must not forget or replay that unresolved transaction.
            Fresh(Kinds.Containers,defaultInteractives:true,keepOnExit:retain,
                directional:true,checkIntervalSeconds:.05f);
            Peer(1);Peer(7,64);data=Object(typeof(Container));view=ZNetScene.instance.FindInstance(data);
            chest=view.GetComponent<Container>();queries=0;view.OnInvoke=delegate{queries++;};Tick(0);
            node=controller.RequestNode(data.m_uid,"RPC_RequestOpen".GetStableHashCode());
            chest.InUse=true;Tick(.1f);Tick(2.1f);
            Check("local-use parking never discards an unanswered native source transaction",queries==1&&!node.WaitingForRequest&&data.GetOwner()==7);
            Import(data,99,false);controller.InventoryResponse(chest,7,true);chest.InUse=false;Tick(2.2f);
            Check("late native receipt completes after local use without resending",queries==1&&data.GetOwner()==1);
        }

        // Mixed refusal reasons share one cell but not one cooldown.
        Fresh(Kinds.Containers|Kinds.Doors,defaultInteractives:true,directional:true,checkIntervalSeconds:.05f);
        var actor=Peer(1);var source=Peer(7,64);FiresGhettoNetworkMod.FgnVersionGate.Match=true;
        var mixedChest=Object(typeof(Container));var mixedView=ZNetScene.instance.FindInstance(mixedChest);
        var inventory=mixedView.GetComponent<Container>();var door=Object(typeof(Door));
        int chestQueries=0,doorQueries=0;
        mixedView.OnInvoke=delegate{chestQueries++;controller.InventoryResponse(inventory,7,false);};
        source.m_rpc.OnInvoke=delegate(string method,object[] args)
        {doorQueries++;controller.Cooperative.Result(source.m_rpc,(ZDOID)args[0],false,"held");};
        Tick(0);ZDOMan.instance.GetZDO(actor.m_characterID).Yaw=90;Tick(.1f);
        Check("a chest cooldown cannot suppress another object's fresh request in the same cell",chestQueries==1&&doorQueries==2);
        controller.Unregister(mixedView);controller.Unregister(ZNetScene.instance.FindInstance(door));
        Check("unloading mixed suppressed/refused objects clears the sparse listener",Index().Grid.Renewed==null);

        Fresh(Kinds.Containers,defaultInteractives:true,directional:true,checkIntervalSeconds:.05f);
        actor=Peer(1);Peer(7,64);Peer(8,64);var changed=Object(typeof(Container));
        var changedView=ZNetScene.instance.FindInstance(changed);inventory=changedView.GetComponent<Container>();int changes=0;
        changedView.OnInvoke=delegate(long target,string method,object[] args)
        {changes++;controller.InventoryResponse(inventory,target,false);};
        Tick(0);changed.SetOwner(8);controller.OwnerChanged(changed);
        ZDOMan.instance.GetZDO(actor.m_characterID).Yaw=90;Tick(.1f);
        Check("a different source owner does not inherit the previous source's refusal gate",changes==2&&changed.GetOwner()==8);

        Fresh(Kinds.Containers,defaultInteractives:true,directional:true,checkIntervalSeconds:.05f);
        actor=Peer(1);Peer(7,64);var moved=Object(typeof(Container));var movedView=ZNetScene.instance.FindInstance(moved);
        inventory=movedView.GetComponent<Container>();int movedQueries=0;
        movedView.OnInvoke=delegate{movedQueries++;controller.InventoryResponse(inventory,7,false);};
        Tick(0);moved.Position=new Vector3(8.1f,0,1);movedView.transform.position=moved.Position;controller.Position(moved);Tick(.1f);
        Check("object movement cannot bypass a still-active inventory refusal gate",movedQueries==1&&controller.RequestNode(moved.m_uid,"RPC_RequestOpen".GetStableHashCode()).WaitingForRequest);
        ZDOMan.instance.GetZDO(actor.m_characterID).Yaw=90;Tick(1.1f);
        Check("fresh post-gate coverage retries in the object's new cell",movedQueries==2);
    }
}
