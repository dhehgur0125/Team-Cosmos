/* [ 코드 설명 ] 손전등을 처음 켤 때 화면이 잠깐 하늘색으로 변하거나(에디터) 멈칫하는(빌드) 문제를 막습니다.
   손전등 빛은 (1) 둥근 무늬(쿠키)가 붙어 있고 (2) 부드러운 그림자를 만듭니다. 켜지는 순간 모든 물체가
   "빛 무늬 + 부드러운 그림자를 읽는" 그리기 방식으로 바뀌는데, 처음 바뀔 때 그 방식을 물체마다 새로 준비하느라
   시간이 걸립니다 (에디터는 준비되는 동안 하늘색으로 그림, 빌드는 잠깐 멈춤).
   그래서 게임 시작부터 눈에 보이지 않는 빛 하나를 카메라 자리에 켜 두어, 처음부터 모든 물체가
   손전등을 켰을 때와 같은 방식으로 그려지게 합니다. 이렇게 하면 손전등을 켜도 바뀌는 게 없습니다.
   - 밝기 0.001 (손전등은 75)이라 화면에 보이지 않습니다.
   - 발밑·앞바닥을 향해 그림자를 만들어야 "부드러운 그림자" 방식이 유지됩니다 (가장 작은 그림자 칸 256 사용). */
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VoidLast.Player
{
    public static class LightCookieKeeper
    {
        static Light keeper;

        /// <summary>손전등이 깨어날 때 부른다. 한 번 만들면 씬이 바뀌어도 남는다.</summary>
        public static void Ensure(Light source)
        {
            if (keeper != null || source == null || source.cookie == null) return;
            var go = new GameObject("Light Cookie Keeper") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            keeper = go.AddComponent<Light>();
            keeper.type = LightType.Spot;
            keeper.cookie = source.cookie;          // 같은 무늬를 써서 무늬 저장 공간을 따로 차지하지 않음
            keeper.spotAngle = 100f;
            keeper.innerSpotAngle = 80f;
            keeper.range = 3f;
            keeper.intensity = 0.001f;
            keeper.color = Color.white;
            keeper.shadows = source.shadows == LightShadows.None ? LightShadows.Soft : source.shadows;
            keeper.shadowNearPlane = 0.1f;
            keeper.renderMode = LightRenderMode.ForcePixel;
            keeper.lightmapBakeType = LightmapBakeType.Realtime;
            var data = go.GetComponent<UniversalAdditionalLightData>();
            if (data == null) data = go.AddComponent<UniversalAdditionalLightData>();
            // 그림자 칸 크기 "낮음"(256). 공개 설정 함수가 없어 내부 값을 바꾼다 (실패해도 동작에는 문제없음)
            var tier = typeof(UniversalAdditionalLightData).GetField("m_AdditionalLightsShadowResolutionTier",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (tier != null) tier.SetValue(data, UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow);
            RenderPipelineManager.beginCameraRendering += Follow;
        }

        /// <summary>주 카메라 자리로 옮겨 앞쪽 바닥을 향하게 한다 (그 카메라에서 "보이는 그림자 빛"이 되도록)</summary>
        static void Follow(ScriptableRenderContext context, Camera cam)
        {
            if (keeper == null) { RenderPipelineManager.beginCameraRendering -= Follow; return; }
            // 게임의 주 카메라만 따라간다. 한 프레임에 여러 카메라(에디터 씬 뷰 등) 사이를 오가면
            // GPU 가림 판정(GPU Resident Drawer)이 그림자 빛의 정보를 잘못 재사용해 오류를 낸다.
            if (cam.cameraType != CameraType.Game || cam != Camera.main) return;
            var t = cam.transform;
            Vector3 flat = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.ProjectOnPlane(t.up, Vector3.up);
            Vector3 dir = (flat.normalized + Vector3.down * 1.2f).normalized;   // 앞쪽 아래 (약 50도)
            keeper.transform.SetPositionAndRotation(t.position, Quaternion.LookRotation(dir, Vector3.up));
        }
    }
}
