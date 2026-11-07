using UnityEngine;
using VoidLast.Player;

/// <summary>
/// 층(씬)을 옮길 때 이어서 가져가는 정보. 씬이 바뀌어도 남는 정적 데이터.
///  - 어느 계단(A/B)으로 내려왔는지 → 다음 층에서 같은 계단 문 앞에 도착 연출
///  - 내 대원의 HP·핫바·탄약·손전등 → 다음 층에서 그대로 이어받기 (죽은 대원은 죽은 채로)
/// 멀티플레이에서는 대원마다 자기 대원 정보만 저장하고 적용한다 (HP·핫바는 주인이 관리하므로).
/// </summary>
public static class FloorCarryOver
{
    /// <summary>다음 층 도착 연출이 남아 있음</summary>
    public static bool Pending;
    /// <summary>층 이동 중 (이전 층 씬이 내려가는 동안 음성 채널 유지 등)</summary>
    public static bool Transitioning;
    /// <summary>'A' 또는 'B' (계단 이름)</summary>
    public static char ArrivalSide;
    public static int FromFloor;

    public static bool HasPlayerState;
    public static float Health;
    public static bool Dead;
    public static readonly FarmingItemKind[] Items = new FarmingItemKind[FpsInventory.Capacity];
    public static int Magazine, Reserve;
    public static bool Flashlight;
    /// <summary>죽은 채로 다음 층에 도착함 (관전 모드로 바로 넘어가기)</summary>
    public static bool ArrivedDead;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => ResetAll();

    /// <summary>새 게임을 시작할 때 (로비) 모두 지운다</summary>
    public static void ResetAll()
    {
        Pending = false; Transitioning = false; ArrivalSide = '\0'; FromFloor = 0;
        HasPlayerState = false; Health = 0; Dead = false; ArrivedDead = false;
        for (int i = 0; i < Items.Length; i++) Items[i] = FarmingItemKind.None;
        Magazine = Reserve = 0; Flashlight = false;
    }

    public static void SavePlayer(GameObject player)
    {
        if (player == null) return;
        var health = player.GetComponent<FpsHealth>();
        var inv = player.GetComponent<FpsInventory>();
        var pistol = player.GetComponent<FpsPistol>();
        var light = player.GetComponent<FpsWeaponFlashlight>();
        if (health == null) return;
        HasPlayerState = true;
        Health = health.Current;
        Dead = health.IsDead;
        // 임무 물건은 그 층에서만 쓴다: 아래층으로 가져가지 않는다
        for (int i = 0; i < Items.Length; i++)
        {
            var kind = inv != null ? inv.ItemAt(i) : FarmingItemKind.None;
            Items[i] = FarmingItems.IsMissionItem(kind) ? FarmingItemKind.None : kind;
        }
        if (pistol != null) { Magazine = pistol.Magazine; Reserve = pistol.Reserve; }
        Flashlight = light != null && light.On;
    }

    public static void ApplyPlayer(GameObject player)
    {
        ArrivedDead = false;
        if (player == null || !HasPlayerState) return;
        var health = player.GetComponent<FpsHealth>();
        var inv = player.GetComponent<FpsInventory>();
        var pistol = player.GetComponent<FpsPistol>();
        var light = player.GetComponent<FpsWeaponFlashlight>();
        if (inv != null) inv.RestoreItems(Items);
        if (pistol != null) pistol.RestoreAmmo(Magazine, Reserve);
        if (light != null && light.On != Flashlight && !Dead) light.Toggle();
        if (health != null)
        {
            ArrivedDead = Dead;
            health.RestoreFromCarryOver(Dead ? 0f : Mathf.Max(1f, Health));
        }
    }
}
