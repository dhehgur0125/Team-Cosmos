/* [ 코드 설명 ] 몬스터 HP와 실제 명중 피해를 관리하고 피격자·피격 시점의 공격자 위치를 AI에 전달합니다.
   체력이 0이 되면 사라지지 않고 그 자리에서 그로기(쓰러짐)에 걸리고, 밸런스 설정(GameBalance)의 2~3분 사이 무작위 시간 뒤 체력 100%로 다시 일어납니다.
   일어나기 몇 초 전(기본 10초)에 WakingSoon 이벤트가 나서 MonsterWakeWarning이 꿈틀거림·소리를 냅니다.
   HP·속도·피해는 Awake에서 GameBalance 값(인원별 HP 배율 포함)으로 바뀝니다. useBalance를 끄면 프리팹 값을 그대로 씁니다.
   쓰러질 때 Died, 다시 일어날 때 Revived 이벤트가 납니다. 그로기 중에는 IsDead가 true라서 AI·공격·스턴이 모두 멈춥니다. */
using System;
using UnityEngine;
using VoidLast.Player;

namespace VoidLast.Monster
{
    public sealed class MonsterHealth : MonoBehaviour, IFpsDamageTarget
    {
        [SerializeField, Min(1)] float maximumHealth = 300;
        [SerializeField, Min(0), Tooltip("useBalance를 끈 경우에만: 체력이 0이 된 뒤 다시 일어나기까지 걸리는 시간(초). 0이면 다시 일어나지 않는다.")] float groggyDuration = 30;
        [SerializeField, Tooltip("HP·속도·피해·그로기 시간을 밸런스 설정(Resources/GameBalance)에서 가져온다")] bool useBalance = true;
        float reviveAt;
        bool warned;
        public float Maximum => maximumHealth;
        public float Current { get; private set; }
        public bool IsDead => Current <= 0;
        public GameBalance.MonsterKind Kind { get; private set; }
        public event Action<GameObject, Vector3> Damaged;
        public event Action Died;
        /// <summary>그로기가 끝나 체력 100%로 다시 일어났을 때</summary>
        public event Action Revived;
        /// <summary>곧 일어난다 (일어나기 wakeWarningSeconds 전). 복제본은 MonsterNetworkSync가 대신 알린다</summary>
        public event Action WakingSoon;
        /// <summary>그로기가 끝날 때까지 남은 시간(초). 쓰러져 있지 않으면 0 (복제본에서는 알 수 없어 0)</summary>
        public float GroggyRemaining => IsDead && reviveAt > 0 ? Mathf.Max(0, reviveAt - Time.time) : 0;
        public event Action<Vector3, Vector3> HitConfirmed;
        /// <summary>멀티플레이: 복제본에 맞은 피해는 직접 깎지 않고 서버로 보낸다.</summary>
        public Action<float, GameObject, Vector3> NetworkHitRedirect;

        void Awake()
        {
            Kind = GameBalance.KindOf(gameObject);
            if (useBalance) maximumHealth = GameBalance.ApplyToMonster(gameObject, maximumHealth);
            Current = maximumHealth;
            if (GetComponent<MonsterWakeWarning>() == null) gameObject.AddComponent<MonsterWakeWarning>();
        }

        void Update()
        {
            // 그로기 시간은 방장(또는 혼자 실행)에서만 잰다. 복제본은 서버의 HP가 돌아오면 일어난다.
            if (reviveAt <= 0 || !IsDead || NetworkHitRedirect != null) return;
            if (!warned && Time.time >= reviveAt - GameBalance.Current.wakeWarningSeconds) { warned = true; WakingSoon?.Invoke(); }
            if (Time.time >= reviveAt) Revive();
        }

        /// <summary>복제본 전용: 서버가 "곧 일어난다"고 알려 왔을 때</summary>
        public void RaiseWakingSoon() { if (IsDead) WakingSoon?.Invoke(); }

        /// <summary>그로기를 끝내고 체력 100%로 다시 일어난다.</summary>
        public void Revive()
        {
            if (!IsDead) return;
            reviveAt = 0; warned = false; Current = maximumHealth;
            Revived?.Invoke();
        }

        public void TakeHit(float damage, GameObject attacker, Vector3 hitPoint)
        {
            if (IsDead || damage <= 0 || float.IsNaN(damage)) return;
            if (NetworkHitRedirect != null) { NetworkHitRedirect(damage, attacker, hitPoint); return; }
            Current = Mathf.Max(0, Current - damage);
            Vector3 outward = attacker != null ? (attacker.transform.position + Vector3.up - hitPoint).normalized : -transform.forward;
            HitConfirmed?.Invoke(hitPoint, outward);
            if (IsDead)
            {
                float seconds = useBalance ? GameBalance.RandomGroggySeconds() : groggyDuration;
                reviveAt = seconds > 0 ? Time.time + seconds : 0;
                warned = false;
                PlayLog.MonsterDowned(Kind);
                Died?.Invoke();
            }
            else Damaged?.Invoke(attacker, attacker != null ? attacker.transform.position : hitPoint);
        }

        /// <summary>복제본 전용: 서버의 HP를 반영한다. 0이 되면 쓰러짐(Died), 0에서 다시 오르면 일어남(Revived) 이벤트를 낸다.</summary>
        public void ApplyNetworkHealth(float value)
        {
            if (float.IsNaN(value)) return;
            bool wasDead = IsDead;
            Current = Mathf.Clamp(value, 0, maximumHealth);
            if (!wasDead && IsDead) Died?.Invoke();
            else if (wasDead && !IsDead) Revived?.Invoke();
        }
    }
}
