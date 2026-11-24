using System;
using UnityEngine;
using VoidLast.Monster;

/// <summary>
/// 밸런스 수치를 한곳에 모은 설정 파일 (Resources/GameBalance.asset).
/// 기획서 "VOID LAST 밸런스 패치 제안"(claude.ai/artifact/P63MB7ZfYKxn6pog1m9FBB)의 값. Inspector에서 바꾸면 다음 실행부터 적용된다.
///  - 몬스터: HP·추격 속도·공격 피해 (프리팹 값 대신 이 값을 쓴다), 맞으면 느려짐·움찔, 안 맞으면 체력 회복, 쓰러지면 다시 일어나는지
///  - 대원: 달리기 지구력
///  - 버티기 임무: 소음 반경·간격
///  - 인원별 보정: 게임을 시작할 때의 인원(StartPlayers)으로 정하고, 도중에 누가 나가도 바꾸지 않는다
/// 파일이 없으면 아래 기본값을 쓴다.
/// </summary>
[CreateAssetMenu(fileName = "GameBalance", menuName = "VOID LAST/Game Balance")]
public sealed class GameBalance : ScriptableObject
{
    public enum MonsterKind { Small, Frog, Tentacle, EyedLarge, BlindLarge, Unknown }

    [Serializable]
    public struct MonsterStats
    {
        [Min(1)] public float health;
        [Tooltip("쫓아올 때 속도 (m/초). 눈 없는 대형은 소리 난 곳으로 갈 때 속도")]
        [Min(0.1f)] public float chaseSpeed;
        [Tooltip("공격 한 번의 피해 (개구리는 할퀴기)")]
        [Min(0)] public float damage;
        [Tooltip("총에 맞을 때마다: 이 비율(0~1)의 속도로 느려진다")]
        [Range(0, 1)] public float hitSlowTo;
        [Tooltip("총에 맞을 때마다: 느려지는 시간 (초)")]
        [Min(0)] public float hitSlowSeconds;
        [Tooltip("움찔: 짧은 시간(flinchWindow) 안에 이만큼 맞으면 멈칫한다. 0이면 움찔하지 않음")]
        [Min(0)] public int flinchHits;
        [Tooltip("움찔: 멈추는 시간 (초)")]
        [Min(0)] public float flinchSeconds;
    }

    [Header("몬스터 (3인 기준. 인원별 HP 배율은 아래)")]
    // 기획서 "VOID LAST 몬스터 위협 강화 기획"(claude.ai/artifact/LgFmmPzFSYDP1qUa2joJU2) 값
    public MonsterStats small = new MonsterStats { health = 300, chaseSpeed = 4.8f, damage = 15, hitSlowTo = 0.4f, hitSlowSeconds = 0.4f, flinchHits = 3, flinchSeconds = 1f };
    public MonsterStats frog = new MonsterStats { health = 450, chaseSpeed = 3.5f, damage = 20, hitSlowTo = 0.4f, hitSlowSeconds = 0.4f, flinchHits = 4, flinchSeconds = 1f };
    [Tooltip("개구리 점프 착지 피해")]
    [Min(0)] public float frogLandDamage = 40;
    public MonsterStats tentacle = new MonsterStats { health = 900, chaseSpeed = 3.6f, damage = 25, hitSlowTo = 0.5f, hitSlowSeconds = 0.3f, flinchHits = 5, flinchSeconds = 0.8f };
    public MonsterStats eyedLarge = new MonsterStats { health = 1200, chaseSpeed = 4.1f, damage = 40, hitSlowTo = 0.6f, hitSlowSeconds = 0.25f, flinchHits = 6, flinchSeconds = 0.8f };
    public MonsterStats blindLarge = new MonsterStats { health = 1800, chaseSpeed = 1.8f, damage = 60, hitSlowTo = 0.7f, hitSlowSeconds = 0.2f, flinchHits = 8, flinchSeconds = 0.6f };

    [Header("맞으면 움찔")]
    [Tooltip("이 시간(초) 안에 flinchHits만큼 맞으면 움찔")]
    [Min(0.1f)] public float flinchWindow = 3;
    [Tooltip("움찔한 뒤 이 시간(초) 동안은 다시 움찔하지 않는다 (계속 쏴서 묶어 두기 방지)")]
    [Min(0)] public float flinchCooldown = 3;

    [Header("안 맞으면 체력 회복")]
    public bool monsterRegen = true;
    [Tooltip("마지막으로 맞은 뒤 이 시간(초)이 지나면 회복 시작")]
    [Min(0)] public float regenDelay = 30;
    [Tooltip("0에서 가득 찰 때까지 걸리는 시간 (초)")]
    [Min(0.1f)] public float regenSeconds = 20;

    [Header("쓰러짐")]
    [Tooltip("끄면(기본) 한 번 쓰러진 몬스터는 다시 일어나지 않는다 (그 층에 다시 오면 처음부터 있음). 켜면 아래 시간 뒤 일어난다")]
    public bool monstersRevive = false;
    [Tooltip("monstersRevive일 때만: 쓰러진 뒤 다시 일어나기까지 (초). 이 사이에서 무작위")]
    [Min(1)] public float groggyMinSeconds = 120;
    [Min(1)] public float groggyMaxSeconds = 180;
    [Tooltip("일어나기 몇 초 전부터 꿈틀거리고 소리를 내는지")]
    [Min(0)] public float wakeWarningSeconds = 10;

    [Header("대원 지구력")]
    [Tooltip("가득 찬 상태에서 계속 달릴 수 있는 시간 (초)")]
    [Min(0.5f)] public float runSeconds = 7;
    [Tooltip("다 쓰면 이만큼(0~1) 찰 때까지 달릴 수 없다")]
    [Range(0, 1)] public float exhaustedResume = 0.4f;
    [Tooltip("걸을 때 초당 차는 양 (0~1)")]
    [Min(0)] public float walkRecoverPerSecond = 0.15f;
    [Tooltip("서 있거나 앉아 있을 때 초당 차는 양 (0~1)")]
    [Min(0)] public float restRecoverPerSecond = 0.25f;
    [Tooltip("점프 한 번에 쓰는 양 (0~1)")]
    [Range(0, 1)] public float jumpCost = 0.1f;

    [Header("버티기 임무 소음 (2~4층. 1층 점화 소음은 그대로)")]
    [Min(5)] public float holdoutNoiseRadius = 30;
    [Min(3)] public float holdoutNoiseInterval = 35;
    [Tooltip("남은 시간이 이 초 이하면 아래 간격으로 더 자주 울린다")]
    [Min(0)] public float holdoutFinalSeconds = 30;
    [Min(3)] public float holdoutFinalInterval = 10;

    [Header("인원별 보정 (1인, 2인, 3인, 4인)")]
    public float[] monsterHealthByPlayers = { 0.4f, 0.7f, 1f, 1.3f };
    public int[] ammoPacksByPlayers = { 2, 3, 4, 5 };
    public int[] syringesByPlayers = { 2, 3, 3, 4 };
    [Tooltip("버티기 임무 시간 배율 (발전기·내려받기·연료 충전·점화)")]
    public float[] holdoutByPlayers = { 0.7f, 0.9f, 1f, 1f };
    [Tooltip("두 장치를 함께 작동할 때 시간 제한 (초). 살아 있는 대원이 혼자면 제한 없음")]
    public float[] syncWindowByPlayers = { 10f, 10f, 10f, 8f };

    [Header("시험")]
    [Tooltip("에디터에서 층 씬을 바로 실행할 때(오프라인) 몇 인 기준으로 할지")]
    [Range(1, 4)] public int offlinePlayers = 1;
    [Tooltip("판 기록 파일 남기기 (persistentDataPath/PlayLogs)")]
    public bool writePlayLogs = true;

    // ------------------------------------------------------------------ 불러오기
    static GameBalance _current;
    /// <summary>게임을 시작할 때의 인원 (1~4). 로비에서 정한다. 0이면 아직 모름 → 오프라인 값</summary>
    public static int StartPlayers;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { _current = null; StartPlayers = 0; }

    public static GameBalance Current
    {
        get
        {
            if (_current == null)
            {
                _current = Resources.Load<GameBalance>("GameBalance");
                if (_current == null) _current = CreateInstance<GameBalance>();
            }
            return _current;
        }
    }

    /// <summary>보정에 쓰는 인원 (1~4)</summary>
    public static int Players => Mathf.Clamp(StartPlayers > 0 ? StartPlayers : Current.offlinePlayers, 1, 4);

    static T ByPlayers<T>(T[] table, T fallback) => table != null && table.Length > 0 ? table[Mathf.Clamp(Players - 1, 0, table.Length - 1)] : fallback;

    public static float MonsterHealthScale => ByPlayers(Current.monsterHealthByPlayers, 1f);
    public static float HoldoutScale => ByPlayers(Current.holdoutByPlayers, 1f);
    public static float SyncWindow => ByPlayers(Current.syncWindowByPlayers, 10f);

    /// <summary>n번째(1부터) 탄알집이 놓이려면 필요한 최소 인원 (5 = 놓이지 않음)</summary>
    public static int MinPlayersForAmmo(int n) => MinPlayers(Current.ammoPacksByPlayers, n);
    public static int MinPlayersForSyringe(int n) => MinPlayers(Current.syringesByPlayers, n);

    static int MinPlayers(int[] table, int n)
    {
        for (int p = 1; p <= 4; p++) if (table != null && table.Length >= p && table[p - 1] >= n) return p;
        return 5;
    }

    /// <summary>버티기 임무: 다음 소음까지 기다릴 시간 (남은 시간이 짧으면 더 자주)</summary>
    public static float NextHoldoutNoise(float remainingSeconds)
    {
        var b = Current;
        return remainingSeconds <= b.holdoutFinalSeconds ? b.holdoutFinalInterval : b.holdoutNoiseInterval;
    }

    /// <summary>쓰러진 뒤 다시 일어나기까지 (초). 0 = 다시 일어나지 않음</summary>
    public static float RandomGroggySeconds()
    {
        var b = Current;
        if (!b.monstersRevive) return 0;
        return UnityEngine.Random.Range(Mathf.Min(b.groggyMinSeconds, b.groggyMaxSeconds), Mathf.Max(b.groggyMinSeconds, b.groggyMaxSeconds));
    }

    // ------------------------------------------------------------------ 몬스터
    public static MonsterKind KindOf(GameObject monster)
    {
        if (monster == null) return MonsterKind.Unknown;
        if (monster.GetComponent<SmallMonsterAI>() != null) return MonsterKind.Small;
        if (monster.GetComponent<FrogMonsterAI>() != null) return MonsterKind.Frog;
        if (monster.GetComponent<BlindMonsterAI>() != null) return MonsterKind.Tentacle;
        if (monster.GetComponent<EyedMonsterAI>() != null) return MonsterKind.EyedLarge;
        if (monster.GetComponent<BlindLargeMonsterAI>() != null) return MonsterKind.BlindLarge;
        return MonsterKind.Unknown;
    }

    public static string KindName(MonsterKind kind)
    {
        switch (kind)
        {
            case MonsterKind.Small: return "쪼꼬미";
            case MonsterKind.Frog: return "개구리";
            case MonsterKind.Tentacle: return "촉수형";
            case MonsterKind.EyedLarge: return "눈 있는 대형";
            case MonsterKind.BlindLarge: return "눈 없는 대형";
            default: return "기타";
        }
    }

    public static bool TryGetStatsOf(MonsterKind kind, out MonsterStats stats) => Current.TryGetStats(kind, out stats);

    public bool TryGetStats(MonsterKind kind, out MonsterStats stats)
    {
        switch (kind)
        {
            case MonsterKind.Small: stats = small; return true;
            case MonsterKind.Frog: stats = frog; return true;
            case MonsterKind.Tentacle: stats = tentacle; return true;
            case MonsterKind.EyedLarge: stats = eyedLarge; return true;
            case MonsterKind.BlindLarge: stats = blindLarge; return true;
            default: stats = default; return false;
        }
    }

    /// <summary>몬스터 HP(인원 배율 포함)·속도·피해를 이 설정 값으로 바꾼다. MonsterHealth.Awake가 부른다 (모든 화면에서 같게)</summary>
    public static float ApplyToMonster(GameObject monster, float prefabHealth)
    {
        var kind = KindOf(monster);
        if (!Current.TryGetStats(kind, out var s)) return prefabHealth;
        var melee = monster.GetComponent<MonsterMelee>();
        switch (kind)
        {
            case MonsterKind.Small: monster.GetComponent<SmallMonsterAI>().ApplyBalance(s.chaseSpeed); if (melee != null) melee.ApplyBalance(s.damage); break;
            case MonsterKind.Frog: monster.GetComponent<FrogMonsterAI>().ApplyBalance(s.chaseSpeed, Current.frogLandDamage); if (melee != null) melee.ApplyBalance(s.damage); break;
            case MonsterKind.Tentacle: monster.GetComponent<BlindMonsterAI>().ApplyBalance(s.chaseSpeed, s.damage); break;
            case MonsterKind.EyedLarge: monster.GetComponent<EyedMonsterAI>().ApplyBalance(s.chaseSpeed); if (melee != null) melee.ApplyBalance(s.damage); break;
            case MonsterKind.BlindLarge: monster.GetComponent<BlindLargeMonsterAI>().ApplyBalance(s.chaseSpeed, s.damage); break;
        }
        return Mathf.Max(1f, Mathf.Round(s.health * MonsterHealthScale));
    }
}
