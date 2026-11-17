using UnityEngine;

/// <summary>
/// 층 임무 공통 창구. 그 층의 임무 진행자(F2MissionDirector, F3MissionDirector)가 켜질 때 등록한다.
/// 층 이동(FloorTransition)과 고치(CocoonSystem)는 어느 층 임무인지 몰라도 이것만 보면 된다.
/// 임무가 없는 층은 Current가 비어 있다 (계단이 바로 열림).
/// </summary>
public interface IFloorMission
{
    /// <summary>클리어 조건을 채워서 아래층으로 내려갈 수 있는지</summary>
    bool ExitUnlocked { get; }
    /// <summary>아직 잠겨 있어 들어갈 수 없는 곳인지 (고치가 생기면 안 되는 곳)</summary>
    bool IsInLockedArea(Vector3 point);
}

public static class FloorMission
{
    public static IFloorMission Current { get; private set; }

    public static void Register(IFloorMission mission) { Current = mission; }
    public static void Unregister(IFloorMission mission) { if (ReferenceEquals(Current, mission)) Current = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Current = null; }
}
