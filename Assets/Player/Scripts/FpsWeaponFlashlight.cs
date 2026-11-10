/* [ 코드 설명 ] 권총 총열 아래에 단 손전등입니다. E로 켜고 끄며, 권총을 들고 있을 때만 빛이 나갑니다(다른 장비로 바꾸면 꺼졌다가 다시 들면 켜짐).
   빛은 1인칭 총에 붙은 스포트라이트(원형 쿠키 텍스처)로 화면 가운데에 또렷한 원을 만들고, 켜져 있는 동안 내 화면 가장자리를 어둡게 해서 시야를 좁힙니다.
   멀티플레이에서는 켜짐 상태만 받아 다른 대원의 손전등도 보이게 하며, 다른 대원 손전등은 그림자를 계산하지 않습니다. */
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace VoidLast.Player
{
    [DisallowMultipleComponent]
    public sealed class FpsWeaponFlashlight : MonoBehaviour
    {
        [Header("빛 (1인칭 총에 붙은 스포트라이트)")]
        [SerializeField] Light beam;
        [SerializeField, Min(0)] float intensity = 75f;
        [Header("렌즈 (켜지면 빛남)")]
        [SerializeField] Renderer[] firstPersonLens;
        [SerializeField] Renderer[] worldLens;
        [Tooltip("손전등 모델 렌더러 (1인칭/3인칭 표시 규칙에 등록)")]
        [SerializeField] Renderer[] firstPersonModel;
        [SerializeField] Renderer[] worldModel;
        [ColorUsage(false, true)] [SerializeField] Color lensOn = new Color(1f, 0.95f, 0.82f) * 6f;
        [Header("화면 가장자리 어둡게 (내 화면만)")]
        [SerializeField, Range(0, 1)] float vignetteStrength = 0.88f;
        [SerializeField, Range(0.1f, 1f)] float vignetteStart = 0.3f;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        FpsInput input;
        FpsEquipment equipment;
        FpsMotor motor;
        AudioSource sfx;
        AudioClip clickOn, clickOff;
        MaterialPropertyBlock block;
        CanvasGroup vignette;
        float flicker = 1f, flickerUntil;
        bool lit;

        /// <summary>스위치가 켜져 있는지 (권총을 넣으면 빛은 꺼지지만 스위치 상태는 유지)</summary>
        public bool On { get; private set; }
        /// <summary>지금 실제로 빛이 나가는지</summary>
        public bool IsLit => lit;
        /// <summary>멀티플레이 복제본이면 입력 대신 네트워크 상태를 받는다</summary>
        public bool IsNetworkProxy { get; private set; }
        public event Action Toggled;

        void Awake()
        {
            input = GetComponent<FpsInput>();
            equipment = GetComponent<FpsEquipment>();
            motor = GetComponent<FpsMotor>();
            block = new MaterialPropertyBlock();
            var visibility = GetComponent<FpsLocalVisibility>();
            if (visibility != null) visibility.AddRenderers(Concat(worldModel, worldLens), Concat(firstPersonModel, firstPersonLens));
            if (beam != null) beam.enabled = false;
            LightCookieKeeper.Ensure(beam);   // 처음 켤 때 하늘색 화면·멈칫 방지
            ApplyLens(0f);
        }

        static Renderer[] Concat(Renderer[] a, Renderer[] b)
        {
            a = a ?? new Renderer[0]; b = b ?? new Renderer[0];
            var r = new Renderer[a.Length + b.Length];
            a.CopyTo(r, 0); b.CopyTo(r, a.Length);
            return r;
        }

        public void SetNetworkProxy(bool proxy)
        {
            IsNetworkProxy = proxy;
            if (beam != null) beam.shadows = proxy ? LightShadows.None : LightShadows.Soft;   // 다른 대원 손전등은 그림자 생략 (성능)
        }

        /// <summary>복제본 전용: 주인의 손전등 스위치 상태</summary>
        public void ApplyNetworkState(bool on)
        {
            if (!IsNetworkProxy || on == On) return;
            On = on;
            if (on) StartFlicker();
        }

        public void Toggle()
        {
            On = !On;
            if (On) StartFlicker();
            PlayClick(On);
            Toggled?.Invoke();
        }

        void Update()
        {
            if (!IsNetworkProxy && input != null && input.isActiveAndEnabled && input.Flashlight && (motor == null || !motor.IsDead)) Toggle();

            bool pistol = equipment != null && equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Pistol;
            lit = On && pistol && (motor == null || !motor.IsDead) && beam != null && beam.transform.gameObject.activeInHierarchy;

            // 켤 때 잠깐 지직거림
            if (Time.time < flickerUntil)
            {
                float t = flickerUntil - Time.time;
                flicker = Mathf.Repeat(t * 37f, 1f) < 0.45f ? 0.15f : 1f;
            }
            else flicker = 1f;

            if (beam != null)
            {
                if (beam.enabled != lit) beam.enabled = lit;
                beam.intensity = intensity * flicker;
            }
            ApplyLens(lit ? flicker : 0f);
            UpdateVignette();
        }

        void StartFlicker() => flickerUntil = Time.time + 0.16f;

        void ApplyLens(float k)
        {
            Color c = lensOn * k;
            Set(firstPersonLens, c);
            Set(worldLens, c);
        }

        void Set(Renderer[] list, Color c)
        {
            if (list == null) return;
            foreach (var r in list)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(EmissionId, c);
                r.SetPropertyBlock(block);
            }
        }

        // ------------------------------------------------------------------ 화면 가장자리 어둡게
        void UpdateVignette()
        {
            bool mine = !IsNetworkProxy && OwnCameraActive();
            float target = mine && lit ? 1f : 0f;
            if (vignette == null)
            {
                if (target <= 0f) return;
                vignette = BuildVignette();
            }
            vignette.alpha = Mathf.MoveTowards(vignette.alpha, target * flicker, Time.deltaTime * 4f);
        }

        bool OwnCameraActive()
        {
            if (beam == null) return false;
            var cam = beam.GetComponentInParent<Camera>();
            return cam != null && cam.isActiveAndEnabled;
        }

        CanvasGroup BuildVignette()
        {
            var go = new GameObject("Flashlight Vignette", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -10;   // HUD 아래
            var group = go.AddComponent<CanvasGroup>();
            group.alpha = 0; group.interactable = false; group.blocksRaycasts = false;
            var imgGo = new GameObject("Vignette", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            imgGo.transform.SetParent(go.transform, false);
            var rt = (RectTransform)imgGo.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = imgGo.GetComponent<RawImage>();
            img.texture = VignetteTexture(vignetteStart, vignetteStrength);
            img.raycastTarget = false;
            return group;
        }

        static Texture2D VignetteTexture(float start, float strength)
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "FlashlightVignette" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2 - 1, dy = (y + 0.5f) / size * 2 - 1;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / 1.2f;
                    float a = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(start, 1f, r)) * strength;
                    px[y * size + x] = new Color32(0, 0, 0, (byte)(Mathf.Clamp01(a) * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }

        // ------------------------------------------------------------------ 딸깍 소리
        void PlayClick(bool on)
        {
            if (sfx == null)
            {
                sfx = gameObject.AddComponent<AudioSource>();
                sfx.playOnAwake = false; sfx.spatialBlend = 0f; sfx.volume = 0.5f;
                GameAudio.Route(sfx);
                clickOn = Click("FlashlightOn", 2400f, 0.035f);
                clickOff = Click("FlashlightOff", 1700f, 0.03f);
            }
            sfx.PlayOneShot(on ? clickOn : clickOff);
        }

        static AudioClip Click(string name, float freq, float seconds)
        {
            int rate = 44100, n = Mathf.Max(1, (int)(rate * seconds));
            var data = new float[n];
            var rng = new System.Random(3);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Exp(-t * 9f);
                data[i] = (Mathf.Sin(2 * Mathf.PI * freq * i / rate) * 0.6f + (float)(rng.NextDouble() * 2 - 1) * 0.4f) * env * 0.6f;
            }
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
