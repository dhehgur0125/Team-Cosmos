/* [ 코드 설명 ]
1. 분리된 탄창 메시의 물리 낙하와 자동 제거만 처리합니다.
2. 캐릭터 충돌은 무시하고 환경 바닥에는 충돌합니다. 탄약·피해·네트워크 오브젝트는 만들지 않습니다.
*/
using UnityEngine;
using UnityEngine.AI;

namespace VoidLast.Player
{
    public sealed class FpsDroppedMagazine : MonoBehaviour
    {
        public Rigidbody Body { get; private set; }
        public bool TouchedEnvironment { get; private set; }
        public static FpsDroppedMagazine Spawn(Transform source, float lifetime)
        {
            if (source == null) return null;
            var mesh = source.GetComponent<MeshFilter>(); var renderer = source.GetComponent<Renderer>();
            if (mesh == null || renderer == null) return null;
            var go = new GameObject("Dropped Pistol Magazine"); go.layer = 2;
            go.transform.SetPositionAndRotation(source.position, source.rotation); go.transform.localScale = source.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            var collider = go.AddComponent<BoxCollider>(); collider.center = mesh.sharedMesh.bounds.center; collider.size = mesh.sharedMesh.bounds.size;
            foreach (var player in FindObjectsByType<FpsHealth>(FindObjectsSortMode.None))
                foreach (var other in player.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(collider, other);
            foreach (var agent in FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None))
                foreach (var other in agent.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(collider, other);
            foreach (var other in FindObjectsByType<FpsDroppedMagazine>(FindObjectsSortMode.None)) Physics.IgnoreCollision(collider, other.GetComponent<Collider>());
            var effect = go.AddComponent<FpsDroppedMagazine>(); var body = go.AddComponent<Rigidbody>(); effect.Body = body;
            body.mass = 0.08f; body.linearDamping = 0.2f; body.angularDamping = 0.8f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; body.interpolation = RigidbodyInterpolation.Interpolate;
            body.maxDepenetrationVelocity = 1; body.maxAngularVelocity = 6;
            body.linearVelocity = Vector3.down * 0.6f; body.angularVelocity = source.right * 1.2f;
            Destroy(go, Mathf.Max(0.1f, lifetime)); return effect;
        }
        void OnCollisionEnter(Collision collision) => TouchedEnvironment = true;
    }
}
