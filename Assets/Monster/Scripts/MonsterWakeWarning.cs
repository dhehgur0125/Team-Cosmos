/* [ 코드 설명 ] 쓰러진(그로기) 몬스터가 곧 일어날 때(MonsterHealth.WakingSoon) 몸을 꿈틀거리고 낮게 으르렁거리는 소리를 냅니다.
   그로기가 2~3분 무작위라서, 대원이 모르고 당하지 않도록 일어나기 약 10초 전부터 알려 줍니다. 일어나거나 다시 쓰러지면 멈춥니다.
   MonsterHealth.Awake가 자동으로 붙입니다. 소리는 코드로 만든 임시 소리(우주선 안 3D 소리, 25m까지 들림)입니다. */
using UnityEngine;

namespace VoidLast.Monster
{
    [DisallowMultipleComponent]
    public sealed class MonsterWakeWarning : MonoBehaviour
    {
        [SerializeField, Min(0)] float twitchAngle = 6f;
        [SerializeField, Min(1)] float hearDistance = 25f;
        [SerializeField, Range(0, 1)] float volume = 0.8f;

        static AudioClip _growl;
        MonsterHealth _health;
        Transform _body;
        Quaternion _bodyRest;
        AudioSource _source;
        bool _active;
        float _nextTwitch, _twitchEnd, _nextGrowl;
        Vector3 _twitchAxis;

        void Awake()
        {
            _health = GetComponent<MonsterHealth>();
            var animator = GetComponentInChildren<Animator>(true);
            _body = animator != null && animator.transform != transform ? animator.transform : null;
        }

        void OnEnable() { if (_health != null) { _health.WakingSoon += Begin; _health.Revived += Stop; _health.Died += Stop; } }
        void OnDisable() { if (_health != null) { _health.WakingSoon -= Begin; _health.Revived -= Stop; _health.Died -= Stop; } Stop(); }

        void Begin()
        {
            if (_active) return;
            _active = true;
            if (_body != null) _bodyRest = _body.localRotation;
            _nextTwitch = Time.time; _nextGrowl = Time.time;
        }

        void Stop()
        {
            if (!_active) return;
            _active = false;
            if (_body != null) _body.localRotation = _bodyRest;
            if (_source != null) _source.Stop();
        }

        void Update()
        {
            if (!_active) return;
            if (_health == null || !_health.IsDead) { Stop(); return; }

            // 꿈틀거림: 0.6~1.4초마다 짧게 몸을 떤다
            if (_body != null)
            {
                if (Time.time >= _nextTwitch)
                {
                    _twitchEnd = Time.time + 0.35f;
                    _nextTwitch = Time.time + Random.Range(0.6f, 1.4f);
                    _twitchAxis = Random.onUnitSphere;
                }
                float k = Time.time < _twitchEnd ? Mathf.Sin(Time.time * 55f) * twitchAngle * ((_twitchEnd - Time.time) / 0.35f) : 0f;
                _body.localRotation = _bodyRest * Quaternion.AngleAxis(k, _twitchAxis);
            }

            // 으르렁: 약 3초마다
            if (Time.time >= _nextGrowl)
            {
                _nextGrowl = Time.time + Random.Range(2.5f, 3.5f);
                EnsureSource();
                _source.pitch = Random.Range(0.85f, 1.1f);
                _source.PlayOneShot(Growl(), volume);
            }
        }

        void EnsureSource()
        {
            if (_source != null) return;
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 2f;
            _source.maxDistance = hearDistance;
            _source.dopplerLevel = 0f;
        }

        /// <summary>낮게 떨리는 으르렁 소리 (1.2초, 코드로 만듦)</summary>
        static AudioClip Growl()
        {
            if (_growl != null) return _growl;
            const int rate = 22050; const float seconds = 1.2f;
            int n = (int)(rate * seconds);
            var data = new float[n];
            var rng = new System.Random(7);
            float phase = 0f, rough = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float env = Mathf.Clamp01(t / 0.15f) * Mathf.Clamp01((seconds - t) / 0.4f);
                float freq = 55f + 12f * Mathf.Sin(t * 9f) + 8f * Mathf.Sin(t * 23f);
                phase += 2f * Mathf.PI * freq / rate;
                rough = Mathf.Lerp(rough, (float)(rng.NextDouble() * 2 - 1), 0.08f);
                float saw = (phase / Mathf.PI % 2f) - 1f;
                data[i] = (saw * 0.45f + Mathf.Sin(phase * 0.5f) * 0.35f + rough * 0.6f) * env * (0.7f + 0.3f * Mathf.Sin(t * 31f));
            }
            _growl = AudioClip.Create("MonsterWakeGrowl", n, 1, rate, false);
            _growl.SetData(data, 0);
            return _growl;
        }
    }
}
