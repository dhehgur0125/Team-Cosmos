/* [ 코드 설명 ] 지구력 막대 (HP 막대 바로 아래 얇은 줄)와 숨소리. FpsHud.Awake가 자동으로 붙입니다(프리팹 수정 없음).
   지구력이 가득 차 있으면 막대가 서서히 사라지고, 쓰기 시작하면 나타납니다. 다 써서 못 달리는 동안은 막대가 주황색이 되고 거친 숨소리가 납니다.
   숨소리는 코드로 만든 임시 소리(내 귀에만 들림)입니다. */
using UnityEngine;
using UnityEngine.UI;

namespace VoidLast.Player
{
    [DisallowMultipleComponent]
    public sealed class FpsStaminaHud : MonoBehaviour
    {
        static readonly Color Normal = new Color(0.85f, 0.9f, 0.95f, 0.85f);
        static readonly Color Tired = new Color(1f, 0.55f, 0.2f, 0.95f);
        static AudioClip _breath;

        FpsMotor _motor;
        FpsHealth _health;
        RectTransform _fill;
        Image _fillImage, _back;
        CanvasGroup _group;
        AudioSource _audio;
        float _width, _shownUntil, _nextBreath;

        public void Init(RectTransform healthFill, float width)
        {
            _width = width;
            var root = new GameObject("Stamina", typeof(RectTransform), typeof(CanvasGroup));
            var rt = (RectTransform)root.transform;
            rt.SetParent(healthFill.parent, false);
            rt.anchorMin = rt.anchorMax = healthFill.anchorMin;
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = healthFill.anchoredPosition + new Vector2(0, -healthFill.sizeDelta.y * 0.5f - 8f);
            rt.sizeDelta = new Vector2(width, 5f);
            _group = root.GetComponent<CanvasGroup>();
            _group.alpha = 0f; _group.interactable = false; _group.blocksRaycasts = false;
            _back = MakeImage(rt, "Background", new Color(0, 0, 0, 0.45f));
            _fillImage = MakeImage(rt, "Fill", Normal);
            _fill = _fillImage.rectTransform;
        }

        static Image MakeImage(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(parent.sizeDelta.x, 0);
            var img = go.GetComponent<Image>();
            img.color = color; img.raycastTarget = false;
            return img;
        }

        void Awake() { _motor = GetComponent<FpsMotor>(); _health = GetComponent<FpsHealth>(); }

        void Update()
        {
            if (_motor == null || _fill == null || _motor.IsNetworkProxy) return;
            float s = _motor.Stamina;
            bool dead = _health != null && _health.IsDead;
            _fill.sizeDelta = new Vector2(_width * s, 0);
            _fillImage.color = _motor.Exhausted ? Tired : Normal;
            if (s < 0.999f && !dead) _shownUntil = Time.unscaledTime + 1.2f;
            float target = Time.unscaledTime < _shownUntil ? 1f : 0f;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.unscaledDeltaTime * 3f);

            // 숨소리: 다 써서 못 달리는 동안
            if (_motor.Exhausted && !dead && Time.unscaledTime >= _nextBreath)
            {
                _nextBreath = Time.unscaledTime + Random.Range(0.75f, 0.95f);
                if (_audio == null)
                {
                    _audio = gameObject.AddComponent<AudioSource>();
                    _audio.playOnAwake = false; _audio.spatialBlend = 0f;
                }
                _audio.pitch = Random.Range(0.9f, 1.08f);
                _audio.PlayOneShot(Breath(), 0.35f);
            }
        }

        /// <summary>거친 숨 (들이쉬고 내쉬기, 0.7초, 코드로 만든 바람 소리)</summary>
        static AudioClip Breath()
        {
            if (_breath != null) return _breath;
            const int rate = 22050; const float seconds = 0.7f;
            int n = (int)(rate * seconds);
            var data = new float[n];
            var rng = new System.Random(11);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                // 두 번 숨: 0~0.3초 들이쉼(약하게), 0.35~0.7초 내쉼(강하게)
                float inhale = t < 0.3f ? Mathf.Sin(t / 0.3f * Mathf.PI) * 0.45f : 0f;
                float exhale = t >= 0.33f ? Mathf.Sin((t - 0.33f) / 0.37f * Mathf.PI) : 0f;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp = Mathf.Lerp(lp, noise, 0.25f);
                lp2 = Mathf.Lerp(lp2, lp, 0.35f);
                data[i] = (lp - lp2 * 0.6f) * (inhale + exhale) * 0.9f;
            }
            _breath = AudioClip.Create("StaminaBreath", n, 1, rate, false);
            _breath.SetData(data, 0);
            return _breath;
        }
    }
}
