using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoidLast.Player
{
    // Rewards retain the existing pickup registry IDs and mission activation gates.
    [DisallowMultipleComponent]
    public sealed class LootContainer : MonoBehaviour
    {
        public string containerId;
        public List<FpsWorldPickup> rewards = new List<FpsWorldPickup>();
        public bool searched;
        public FpsWorldPickup randomReward;
        [NonSerialized] public bool seedReady;
        bool randomAllowed;
        public void SetSeed(int seed)
        {
            unchecked { uint h=(uint)seed; foreach(char c in containerId??name)h=(h^c)*16777619; randomAllowed=h%100<50; }
            seedReady=true;
        }
        public static event Action<LootContainer> Searched;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Searched = null;
        public FpsWorldPickup AvailableReward
        {
            get { foreach (var p in rewards) if (p != null && p.isActiveAndEnabled && !p.IsClaimed && FarmingItems.CanLootOnFloor(p.Item,gameObject.scene.name)) return p; return seedReady && randomAllowed && randomReward!=null && randomReward.isActiveAndEnabled && !randomReward.IsClaimed && FarmingItems.CanLootOnFloor(randomReward.Item,gameObject.scene.name) ? randomReward : null; }
        }
        public FpsWorldPickup MissionReward
        {
            get
            {
                foreach(var p in rewards)
                    if(p!=null && p.isActiveAndEnabled && !p.IsClaimed && FarmingItems.IsMissionItem(p.Item)) return p;
                return seedReady && randomAllowed && randomReward!=null && randomReward.isActiveAndEnabled &&
                    !randomReward.IsClaimed && FarmingItems.IsMissionItem(randomReward.Item) ? randomReward : null;
            }
        }
        public bool CanScan => seedReady && MissionReward != null;
        public bool TrySearch(FpsInventory inventory)
        {
            if (inventory == null || inventory.IsNetworkProxy || !inventory.IsAlive) return false;
            if (!seedReady) { LootFeedback.Show("상자 정보를 동기화하고 있습니다. 잠시 후 다시 시도해 주세요."); return false; }
            if (inventory.IsFull) { LootFeedback.Show("인벤토리가 가득 찼습니다. 칸을 비우고 다시 상호작용해 주세요."); return false; }
            var reward = AvailableReward;
            if (reward != null)
            {
                if (!reward.TryCollect(inventory)) { LootFeedback.Show("이 아이템을 더 들 수 없습니다. 인벤토리를 확인해 주세요."); return false; }
                LootFeedback.Show(FarmingItems.HudLabel(reward.Item) + " 획득");
            }
            else LootFeedback.Show(searched ? "이미 수색한 빈 상자입니다." : "상자가 비어 있습니다.");
            if (!searched) { searched = true; Searched?.Invoke(this); }
            return true;
        }
    }
}
