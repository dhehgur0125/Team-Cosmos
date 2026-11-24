using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 모든 클라이언트가 같은 값을 보는 공용 시계.
/// 네트워크 세션 중에는 NGO 서버 시간을, 아니면 로컬 시간을 쓴다.
/// 연출용 오브젝트(비상등, 스파크)는 이 시계와 고정 시드만으로 상태를 계산하므로
/// NetworkObject나 RPC 없이도 최대 4명 모두에게 같은 깜빡임이 보인다.
/// </summary>
public static class SyncedClock
{
    public static double Time
    {
        get
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
                return nm.ServerTime.Time;
            return UnityEngine.Time.timeAsDouble;
        }
    }

    /// <summary>(a, b) 조합에 대해 항상 같은 0~1 값을 돌려주는 해시.</summary>
    public static float Hash01(long a, int b)
    {
        unchecked
        {
            ulong x = (ulong)a * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)b * 0xBF58476D1CE4E5B9UL;
            x ^= x >> 31; x *= 0x94D049BB133111EBUL; x ^= x >> 29;
            return (x & 0xFFFFFF) / (float)0x1000000;
        }
    }

    /// <summary>씬에 배치된 위치로부터 시드를 만든다. 모든 클라이언트에서 동일.</summary>
    public static int SeedFromPosition(Vector3 p)
    {
        unchecked
        {
            int h = Mathf.RoundToInt(p.x * 10f);
            h = h * 73856093 ^ Mathf.RoundToInt(p.y * 10f) * 19349663;
            h = h ^ Mathf.RoundToInt(p.z * 10f) * 83492791;
            return h == 0 ? 1 : h;
        }
    }
}
