using UnityEngine;

namespace RVFX.Tools
{
    public sealed class BulletTrailEmitter : MonoBehaviour
    {
        [Header("Spawn")]
        [SerializeField] private Transform spawnPoint;
        [Range(0f, 1f)]
        [SerializeField] private float spawnProbability = 1f;

        [Header("Lifetime")]
        [SerializeField] private float minLifetime = 0.12f;
        [SerializeField] private float maxLifetime = 0.25f;

        [Header("Direction / Spread (Degrees)")]
        [SerializeField] private float minAngle = 0.0f;
        [SerializeField] private float maxAngle = 2.5f;

        [Header("Raycast")]
        [SerializeField] private float maxDistance = 200f;
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Line Renderer")]
        [SerializeField] private Material material;
        [SerializeField] private float startWidth = 0.03f;
        [SerializeField] private float endWidth = 0.0f;
        [SerializeField] private Gradient colorOverLife;
        [SerializeField] private AnimationCurve widthOverLife = AnimationCurve.Linear(0, 1, 1, 0);

        [Header("Motion")]
        [SerializeField] private float travelSpeed = 300f;

        [Header("Space")]
        [SerializeField] private bool followEmitterTransform = true;

        [Header("Shader Distance")]
        [SerializeField] private bool sendWorldLengthToShader = false;

        private static readonly int TrailLengthId = Shader.PropertyToID("_TrailLength");
        private static readonly int HueShiftId = Shader.PropertyToID("_HueShift");

        private float _hueShift;
        private int _lastSpawnFrame = -1;

        private void Reset()
        {
            spawnPoint = transform;
        }

        private void Awake()
        {
            if (spawnPoint == null) spawnPoint = transform;
        }

        private void OnEnable()
        {
            if (_lastSpawnFrame == Time.frameCount) return;
            _lastSpawnFrame = Time.frameCount;

            if (spawnProbability < 1f && UnityEngine.Random.value > spawnProbability)
                return;

            SpawnInternal();
        }

        public void FireOnce()
        {
            if (_lastSpawnFrame == Time.frameCount) return;
            _lastSpawnFrame = Time.frameCount;

            SpawnInternal();
        }

        public void SetHueShift(float hueShift, bool includeInactiveChildren)
        {
            _hueShift = hueShift;

            if (material != null)
                material.SetFloat(HueShiftId, _hueShift);

            var lrs = GetComponentsInChildren<LineRenderer>(includeInactiveChildren);
            if (lrs == null || lrs.Length == 0) return;

            for (int i = 0; i < lrs.Length; i++)
            {
                var lr = lrs[i];
                if (lr == null) continue;

                var m = lr.material;
                if (m == null) continue;

                m.SetFloat(HueShiftId, _hueShift);
            }
        }

        private void SpawnInternal()
        {
            if (spawnPoint == null) return;

            float life = UnityEngine.Random.Range(minLifetime, Mathf.Max(minLifetime, maxLifetime));

            Vector3 originW = spawnPoint.position;
            Quaternion basisW = spawnPoint.rotation;

            Quaternion spread = RandomConeRotation(minAngle, maxAngle);
            Vector3 dirW = (basisW * spread) * Vector3.forward;

            Vector3 endW = originW + dirW * Mathf.Max(0.01f, maxDistance);

            if (Physics.Raycast(originW, dirW, out var hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore))
                endW = hit.point;

            bool local = followEmitterTransform;

            Vector3 a = local ? transform.InverseTransformPoint(originW) : originW;
            Vector3 b = local ? transform.InverseTransformPoint(endW) : endW;

            var go = new GameObject("BulletTrail");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = !local;
            lr.positionCount = 2;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;

            if (material != null)
            {
                lr.material = new Material(material);
                lr.material.SetFloat(HueShiftId, _hueShift);
            }

            lr.startWidth = startWidth;
            lr.endWidth = endWidth;

            if (colorOverLife != null)
                lr.colorGradient = colorOverLife;

            lr.SetPosition(0, a);
            lr.SetPosition(1, a);

            var inst = go.AddComponent<BulletTrailInstance>();
            inst.Init(lr, a, b, originW, endW, life, travelSpeed, widthOverLife, local, sendWorldLengthToShader);
        }

        private static Quaternion RandomConeRotation(float minAngleDeg, float maxAngleDeg)
        {
            float min = Mathf.Max(0f, minAngleDeg);
            float max = Mathf.Max(min, maxAngleDeg);

            float angle = UnityEngine.Random.Range(min, max);
            float theta = UnityEngine.Random.Range(0f, 360f);

            Quaternion yaw = Quaternion.AngleAxis(theta, Vector3.forward);
            Quaternion pitch = Quaternion.AngleAxis(angle, Vector3.right);
            return yaw * pitch;
        }

        private sealed class BulletTrailInstance : MonoBehaviour
        {
            private LineRenderer _lr;

            private Vector3 _a;
            private Vector3 _b;

            private Vector3 _aWorld;
            private Vector3 _bWorld;

            private float _life;
            private float _age;
            private float _speed;

            private AnimationCurve _widthCurve;
            private float _startWidth;
            private float _endWidth;

            private bool _isLocal;
            private bool _sendWorldLength;

            public void Init(
                LineRenderer lr,
                Vector3 a,
                Vector3 b,
                Vector3 aWorld,
                Vector3 bWorld,
                float life,
                float speed,
                AnimationCurve widthCurve,
                bool isLocal,
                bool sendWorldLength)
            {
                _lr = lr;
                _a = a;
                _b = b;
                _aWorld = aWorld;
                _bWorld = bWorld;

                _life = Mathf.Max(0.01f, life);
                _speed = Mathf.Max(0.01f, speed);
                _widthCurve = widthCurve;

                _startWidth = lr.startWidth;
                _endWidth = lr.endWidth;

                _isLocal = isLocal;
                _sendWorldLength = sendWorldLength;

                UpdateTrailLength(0f);
            }

            private void Update()
            {
                if (_lr == null)
                {
                    Destroy(gameObject);
                    return;
                }

                _age += Time.deltaTime;
                float tLife = Mathf.Clamp01(_age / _life);

                if (_widthCurve != null)
                {
                    float w = Mathf.Clamp01(_widthCurve.Evaluate(tLife));
                    _lr.startWidth = _startWidth * w;
                    _lr.endWidth = _endWidth * w;
                }

                float totalDist = Vector3.Distance(_a, _b);
                float tTravel = totalDist <= 0.0001f ? 1f : Mathf.Clamp01((_age * _speed) / totalDist);

                Vector3 head = Vector3.Lerp(_a, _b, tTravel);
                _lr.SetPosition(0, _a);
                _lr.SetPosition(1, head);

                float currentLengthLocal = Vector3.Distance(_a, head);

                float currentLength = currentLengthLocal;
                if (_sendWorldLength)
                {
                    float totalWorld = Vector3.Distance(_aWorld, _bWorld);
                    float tTravelW = totalWorld <= 0.0001f ? 1f : Mathf.Clamp01((_age * _speed) / totalWorld);
                    Vector3 headW = Vector3.Lerp(_aWorld, _bWorld, tTravelW);
                    currentLength = Vector3.Distance(_aWorld, headW);
                }

                UpdateTrailLength(currentLength);

                if (_age >= _life)
                    Destroy(gameObject);
            }

            private void UpdateTrailLength(float length)
            {
                if (_lr == null) return;

                var m = _lr.material;
                if (m == null) return;

                m.SetFloat(TrailLengthId, Mathf.Max(0.0001f, length));
            }
        }
    }
}