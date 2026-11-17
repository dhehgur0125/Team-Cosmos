/* [ 코드 설명 ] 대원의 발소리를 몬스터에게 알리는 통로입니다. 걷기는 4m, 달리기는 9m까지 들리고 앉아서 움직이면 소리가 나지 않습니다.
   소리를 듣는 몬스터(쪼꼬미·촉수형·눈 없는 대형)가 구독하고, 눈 있는 대형과 개구리는 듣지 않습니다.
   발소리는 0.4초마다 한 번씩 내보내며, 멀티플레이에서는 몬스터 AI를 돌리는 방장 화면에서만 내보냅니다(다른 대원의 움직임은 방장 화면에 동기화되어 있음). */
using System;
using UnityEngine;
using VoidLast.Player;

namespace VoidLast.Monster
{
    public static class MoveNoise
    {
        public const float WalkRadius = 4f, RunRadius = 9f;
        /// <summary>소리가 난 위치, 들리는 반경</summary>
        public static event Action<Vector3, float> Emitted;
        public static void Emit(Vector3 origin, float radius) => Emitted?.Invoke(origin, radius);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var go = new GameObject("MoveNoiseEmitter") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<MoveNoiseEmitter>();
        }
    }

    /// <summary>살아 있는 대원마다 속도를 보고 발소리를 내보낸다 (게임 시작 때 자동으로 만들어짐)</summary>
    sealed class MoveNoiseEmitter : MonoBehaviour
    {
        const float Interval = 0.4f;
        float next;

        void Update()
        {
            if (Time.time < next) return;
            next = Time.time + Interval;
            var network = Unity.Netcode.NetworkManager.Singleton;
            if (network != null && network.IsListening && !network.IsServer) return;   // 몬스터 AI는 방장만 돌린다

            foreach (var motor in FindObjectsByType<FpsMotor>(FindObjectsInactive.Exclude))
            {
                if (motor == null || motor.IsDead || motor.IsCrouching || !motor.IsGrounded) continue;
                float speed = motor.LocalVelocity.magnitude;
                if (speed < motor.WalkSpeed * 0.6f) continue;   // 서 있거나 거의 안 움직임
                bool running = speed > (motor.WalkSpeed + motor.RunSpeed) * 0.5f;
                MoveNoise.Emit(motor.transform.position, running ? MoveNoise.RunRadius : MoveNoise.WalkRadius);
            }
        }
    }
}
