using UnityEngine;

/// <summary>
/// 인원별 보정: 게임을 시작할 때의 인원(GameBalance.Players)이 모자라면 층에 들어올 때 이 물체를 지운다.
///  - 탄알집·주사기: n번째 아이템이 놓이려면 몇 명이 필요한지 GameBalance 표(ammoPacksByPlayers 등)에서 계산 (표를 바꾸면 메뉴를 다시 돌릴 필요 없음)
///  - 그 밖(추가 몬스터 등): minPlayers 이상일 때만 남김
/// FloorTransition.Awake가 모든 화면에서 같은 기준으로 지운다 (네트워크 물체도 생성 전에 지워져 서로 어긋나지 않음).
/// 메뉴 6f(파밍 아이템), 6g(인원별 몬스터)가 붙인다.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerCountGate : MonoBehaviour
{
    public enum Rule { MinPlayers, AmmoPack, Syringe }

    public Rule rule = Rule.MinPlayers;
    [Tooltip("Rule이 MinPlayers일 때: 이 인원 이상이면 남김")]
    [Range(1, 4)] public int minPlayers = 1;
    [Tooltip("탄알집·주사기일 때: 그 층에서 몇 번째 아이템인지 (1부터)")]
    [Min(1)] public int index = 1;

    public int RequiredPlayers
    {
        get
        {
            switch (rule)
            {
                case Rule.AmmoPack: return GameBalance.MinPlayersForAmmo(index);
                case Rule.Syringe: return GameBalance.MinPlayersForSyringe(index);
                default: return minPlayers;
            }
        }
    }

    /// <summary>인원이 모자란 물체를 모두 지운다. 지운 개수를 돌려준다</summary>
    public static int ApplyAll()
    {
        int players = GameBalance.Players, removed = 0;
        foreach (var gate in FindObjectsByType<PlayerCountGate>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (gate == null || players >= gate.RequiredPlayers) continue;
            DestroyImmediate(gate.gameObject);
            removed++;
        }
        if (removed > 0) Debug.Log("[Balance] " + players + "인 기준: 인원이 모자라 " + removed + "개를 뺌");
        return removed;
    }
}
