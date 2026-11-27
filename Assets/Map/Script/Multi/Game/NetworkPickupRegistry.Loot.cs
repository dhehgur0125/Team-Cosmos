using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using VoidLast.Player;

public sealed partial class NetworkPickupRegistry
{
    int lootSeed;
    readonly HashSet<string> searchedContainers=new HashSet<string>();
    IEnumerable<LootContainer> Containers => gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LootContainer>(true));
    void StartLootSession()
    {
        LootContainer.Searched+=OnContainerSearched;
        if(IsServer) { lootSeed=Random.Range(1,int.MaxValue);foreach(var box in Containers)box.SetSeed(lootSeed); }
        else RequestLootStateRpc();
    }
    [Rpc(SendTo.Server)]
    void RequestLootStateRpc(RpcParams rpc=default) => LootStateRpc(lootSeed,string.Join("\n",searchedContainers),RpcTarget.Single(rpc.Receive.SenderClientId,RpcTargetUse.Temp));
    [Rpc(SendTo.SpecifiedInParams)]
    void LootStateRpc(int seed,string searched,RpcParams rpc=default)
    {
        var ids=new HashSet<string>(searched.Split('\n'));
        foreach(var box in Containers) { box.SetSeed(seed);box.searched=ids.Contains(box.containerId); }
    }
    void OnContainerSearched(LootContainer box)
    {
        if(IsSpawned && box.gameObject.scene==gameObject.scene) SearchedContainerRpc(box.containerId);
    }
    [Rpc(SendTo.Server)]
    void SearchedContainerRpc(string id,RpcParams rpc=default)
    {
        var box=Containers.FirstOrDefault(b=>b.containerId==id);
        var player=FpsNetworkPlayer.All.FirstOrDefault(p=>p!=null && p.OwnerClientId==rpc.Receive.SenderClientId);
        if(box==null || player==null || Vector3.Distance(player.transform.position,box.transform.position)>6f)return;
        searchedContainers.Add(id);ContainerSearchedRpc(id);
    }
    [Rpc(SendTo.ClientsAndHost)]
    void ContainerSearchedRpc(string id) { foreach(var box in Containers)if(box.containerId==id)box.searched=true; }
}
