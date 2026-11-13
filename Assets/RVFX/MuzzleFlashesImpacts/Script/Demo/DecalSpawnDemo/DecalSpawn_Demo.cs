using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RVFX.MuzzleFlashesImpacts
{
    public sealed class DecalSpawn_Demo : MonoBehaviour
    {
        public Transform gunTransform;

        public Vector2 aimChangeIntervalRange = new Vector2(0.8f, 1.5f);
        public Vector2 yawRange = new Vector2(-30f, 30f);
        public Vector2 pitchRange = new Vector2(-15f, 15f);
        public float rotateSpeedDegPerSec = 360f;

        public Transform firingPoint;
        public GameObject firingEffectPrefab;
        public Vector2 firingIntervalRange = new Vector2(0.08f, 0.15f);
        public float firingEffectLifeTime = 2f;

        public LayerMask hitLayerMask = ~0;
        public float hitRayDistance = 50f;

        public bool spawnDecals = true;
        public List<GameObject> decalPrefabs = new List<GameObject>();
        public float decalNormalOffset = 0.002f;
        public Vector2 decalRollRange = new Vector2(-15f, 15f);
        public float decalLifeTime = 10f;

        public bool spawnImpacts = true;
        public List<GameObject> impactPrefabs = new List<GameObject>();
        public float impactNormalOffset = 0.002f;
        public Vector2 impactRollRange = new Vector2(-15f, 15f);
        public float impactLifeTime = 2f;

        public bool useStackedNormalOffsetForDecals = true;
        public bool useStackedNormalOffsetForImpacts = false;

        public float stackedMinOffset = 0.002f;
        public float stackedMaxOffset = 0.02f;
        public float stackedStepDistance = 0.001f;

        private Coroutine aimLoop;
        private Coroutine firingLoop;

        private Quaternion baseLocalRotation;
        private Quaternion targetLocalRotation;

        private float _currentStackedOffset;

        private void Awake()
        {
            if (gunTransform == null)
                gunTransform = transform;

            if (firingPoint == null)
                firingPoint = gunTransform;

            baseLocalRotation = gunTransform.localRotation;
            targetLocalRotation = baseLocalRotation;

            ResetStackedNormalOffset();
        }

        private void OnEnable()
        {
            if (gunTransform != null)
                aimLoop = StartCoroutine(CoAimLoop());

            firingLoop = StartCoroutine(CoFiringLoop());
        }

        private void OnDisable()
        {
            if (aimLoop != null)
            {
                StopCoroutine(aimLoop);
                aimLoop = null;
            }

            if (firingLoop != null)
            {
                StopCoroutine(firingLoop);
                firingLoop = null;
            }
        }

        public void ResetStackedNormalOffset()
        {
            _currentStackedOffset = Mathf.Min(stackedMinOffset, stackedMaxOffset);
        }

        private float GetStackedNormalOffset()
        {
            float min = Mathf.Min(stackedMinOffset, stackedMaxOffset);
            float max = Mathf.Max(stackedMinOffset, stackedMaxOffset);
            float step = Mathf.Max(0.00001f, stackedStepDistance);

            if (_currentStackedOffset < min)
                _currentStackedOffset = min;

            float result = _currentStackedOffset;

            _currentStackedOffset += step;
            if (_currentStackedOffset > max)
                _currentStackedOffset = min;

            return result;
        }

        private IEnumerator CoAimLoop()
        {
            while (true)
            {
                float wait = GetRandomRangeSeconds(aimChangeIntervalRange, 0.001f);

                float yaw = Random.Range(Mathf.Min(yawRange.x, yawRange.y), Mathf.Max(yawRange.x, yawRange.y));
                float pitch = Random.Range(Mathf.Min(pitchRange.x, pitchRange.y), Mathf.Max(pitchRange.x, pitchRange.y));

                Quaternion offset = Quaternion.Euler(pitch, yaw, 0f);
                targetLocalRotation = baseLocalRotation * offset;

                float elapsed = 0f;
                while (elapsed < wait)
                {
                    float maxStep = rotateSpeedDegPerSec * Time.deltaTime;
                    gunTransform.localRotation = Quaternion.RotateTowards(gunTransform.localRotation, targetLocalRotation, maxStep);

                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }
        }

        private IEnumerator CoFiringLoop()
        {
            while (true)
            {
                float wait = GetRandomRangeSeconds(firingIntervalRange, 0.001f);
                yield return new WaitForSeconds(wait);

                TrySpawnFiringEffect();
                TrySpawnHitStuff();
            }
        }

        private void ParentToThis(GameObject go)
        {
            if (go == null)
                return;

            go.transform.SetParent(transform, true);
        }

        private void TrySpawnFiringEffect()
        {
            if (firingPoint == null || firingEffectPrefab == null)
                return;

            GameObject go = Instantiate(firingEffectPrefab, firingPoint);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            if (!go.activeSelf)
                go.SetActive(true);

            AttachDestroyAfterDelay(go, firingEffectLifeTime);
        }

        private void TrySpawnHitStuff()
        {
            if (firingPoint == null)
                return;

            if (!spawnDecals && !spawnImpacts)
                return;

            if (!Physics.Raycast(
                firingPoint.position,
                firingPoint.forward,
                out RaycastHit hit,
                Mathf.Max(0.001f, hitRayDistance),
                hitLayerMask,
                QueryTriggerInteraction.Ignore))
                return;

            if (spawnDecals)
                TrySpawnDecalAtHit(hit);

            if (spawnImpacts)
                TrySpawnImpactAtHit(hit);
        }

        private void TrySpawnDecalAtHit(RaycastHit hit)
        {
            if (decalPrefabs == null || decalPrefabs.Count == 0)
                return;

            GameObject prefab = PickRandomPrefab(decalPrefabs);
            if (prefab == null)
                return;

            Vector3 normal = hit.normal;

            Quaternion rot = MakeRotationWithForward(normal, firingPoint != null ? firingPoint.up : Vector3.up);
            float roll = Random.Range(decalRollRange.x, decalRollRange.y);
            rot = Quaternion.AngleAxis(roll, normal) * rot;

            float nOff = useStackedNormalOffsetForDecals ? GetStackedNormalOffset() : decalNormalOffset;
            Vector3 pos = hit.point + normal * nOff;

            GameObject go = Instantiate(prefab, pos, rot);
            ParentToThis(go);

            if (!go.activeSelf)
                go.SetActive(true);

            if (decalLifeTime > 0f)
                AttachDestroyAfterDelay(go, decalLifeTime);
        }

        private void TrySpawnImpactAtHit(RaycastHit hit)
        {
            if (impactPrefabs == null || impactPrefabs.Count == 0)
                return;

            GameObject prefab = PickRandomPrefab(impactPrefabs);
            if (prefab == null)
                return;

            Vector3 normal = hit.normal;

            Quaternion rot = MakeRotationWithForward(normal, firingPoint != null ? firingPoint.up : Vector3.up);
            float roll = Random.Range(impactRollRange.x, impactRollRange.y);
            rot = Quaternion.AngleAxis(roll, normal) * rot;

            float nOff = useStackedNormalOffsetForImpacts ? GetStackedNormalOffset() : impactNormalOffset;
            Vector3 pos = hit.point + normal * nOff;

            GameObject go = Instantiate(prefab, pos, rot);
            ParentToThis(go);

            if (!go.activeSelf)
                go.SetActive(true);

            if (impactLifeTime > 0f)
                AttachDestroyAfterDelay(go, impactLifeTime);
        }

        private static GameObject PickRandomPrefab(List<GameObject> prefabs)
        {
            int count = prefabs != null ? prefabs.Count : 0;
            if (count <= 0) return null;

            for (int i = 0; i < count; i++)
            {
                int idx = Random.Range(0, count);
                if (prefabs[idx] != null)
                    return prefabs[idx];
            }

            return null;
        }

        private static Quaternion MakeRotationWithForward(Vector3 forward, Vector3 upHint)
        {
            forward = forward.sqrMagnitude > 0f ? forward.normalized : Vector3.forward;

            Vector3 up = upHint.sqrMagnitude > 0f ? upHint.normalized : Vector3.up;
            if (Mathf.Abs(Vector3.Dot(up, forward)) > 0.95f)
                up = Vector3.right;

            return Quaternion.LookRotation(forward, up);
        }

        private static void AttachDestroyAfterDelay(GameObject go, float delay)
        {
            DestroyAfterDelay d = go.GetComponent<DestroyAfterDelay>();
            if (d == null)
                d = go.AddComponent<DestroyAfterDelay>();

            d.delay = Mathf.Max(0f, delay);
        }

        private static float GetRandomRangeSeconds(Vector2 range, float minClamp)
        {
            float min = Mathf.Max(minClamp, Mathf.Min(range.x, range.y));
            float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
            return Random.Range(min, max);
        }
    }
}