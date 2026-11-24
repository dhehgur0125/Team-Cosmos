using UnityEngine;
using VoidLast.Player;

/// <summary>고치 Cube 하나. 총에 맞으면 CocoonSystem에 알린다 (쏜 대원 화면에서 판정하고 방장이 횟수를 센다).</summary>
[DisallowMultipleComponent]
public sealed class CocoonTarget : MonoBehaviour, IFpsDamageTarget
{
    public CocoonSystem System { get; set; }
    public ulong PlayerId { get; set; }
    public string Nickname { get; set; }
    public int Hits { get; set; }

    public void TakeHit(float damage, GameObject attacker, Vector3 hitPoint)
    {
        if (System != null) System.ReportHit(PlayerId);
    }
}
