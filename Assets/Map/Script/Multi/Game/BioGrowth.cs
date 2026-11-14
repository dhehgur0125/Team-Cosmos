using UnityEngine;
using VoidLast.Player;

public enum BioGrowthKind
{
    Blocker,   // 길을 막는 촉수. 총으로 부순다
    Burst,     // 밟거나 밑을 지나가면 터지는 덩어리 (웅크리면 안 터짐)
    Hazard     // 둥지 중심부. 가까이 있으면 계속 피해
}

/// <summary>
/// 반응하는 바이오매스 하나. 판정과 동기화는 BioGrowthSystem이 한다 (고치의 CocoonTarget과 같은 구조).
/// InteriorPlacer가 배치표의 #Blockers / #Burst / #Hazard 그룹에 붙인다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BioGrowth : MonoBehaviour, IFpsDamageTarget
{
    [Tooltip("층 안에서 겹치지 않는 번호. 모든 대원 화면에서 같은 물체를 가리키는 데 쓴다")]
    public int id;
    public BioGrowthKind kind;
    [Tooltip("Blocker: 버티는 피해량 (권총 한 발 30)")]
    public float health = 90f;
    [Tooltip("Burst: 터지는 거리 / Hazard: 피해를 입는 거리 (수평, m)")]
    public float radius = 1.3f;

    public BioGrowthSystem System { get; set; }
    public bool Gone { get; private set; }

    public void TakeHit(float damage, GameObject attacker, Vector3 hitPoint)
    {
        if (kind == BioGrowthKind.Blocker && !Gone && System != null) System.ReportDamage(this, damage);
    }

    /// <summary>사라진다. 끄면 붙어 있는 NavMeshObstacle도 꺼져서 몬스터 길이 열린다.</summary>
    public void Remove()
    {
        Gone = true;
        gameObject.SetActive(false);
    }
}
