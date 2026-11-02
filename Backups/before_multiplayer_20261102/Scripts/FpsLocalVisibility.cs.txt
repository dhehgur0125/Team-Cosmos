/* [ 코드 설명 ] 자신의 FPS 카메라에는 1인칭 장비·장갑을, 다른 카메라에는 전신과 손의 권총을 표시합니다. 렌더 콜백에서만 상태를 바꾸고 즉시 복원하며 모델·본·Animator는 유지합니다. */
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace VoidLast.Player
{
    public sealed class FpsLocalVisibility : MonoBehaviour
    {
        [SerializeField] Camera localCamera;
        [SerializeField, FormerlySerializedAs("headRenderers")] Renderer[] worldRenderers;
        [SerializeField] Renderer[] firstPersonRenderers;
        bool[] worldPrevious, firstPersonPrevious;
        bool overridden;
        void OnEnable()
        {
            worldPrevious = new bool[worldRenderers == null ? 0 : worldRenderers.Length];
            firstPersonPrevious = new bool[firstPersonRenderers == null ? 0 : firstPersonRenderers.Length];
            RenderPipelineManager.beginCameraRendering += BeginSrp;
            RenderPipelineManager.endCameraRendering += EndSrp;
            Camera.onPreCull += Begin;
            Camera.onPostRender += End;
        }
        void OnDisable()
        {
            Restore();
            RenderPipelineManager.beginCameraRendering -= BeginSrp;
            RenderPipelineManager.endCameraRendering -= EndSrp;
            Camera.onPreCull -= Begin;
            Camera.onPostRender -= End;
        }
        void BeginSrp(ScriptableRenderContext context, Camera camera) => Begin(camera);
        void EndSrp(ScriptableRenderContext context, Camera camera) => Restore();
        void Begin(Camera camera)
        {
            Restore();
            Apply(worldRenderers, worldPrevious, camera == localCamera);
            Apply(firstPersonRenderers, firstPersonPrevious, camera != localCamera);
            overridden = true;
        }
        void End(Camera camera) => Restore();
        static void Apply(Renderer[] renderers, bool[] previous, bool hide)
        {
            for (int i = 0; i < previous.Length; i++)
                if (renderers[i] != null) { previous[i] = renderers[i].forceRenderingOff; renderers[i].forceRenderingOff = previous[i] || hide; }
        }
        void Restore()
        {
            if (!overridden) return;
            for (int i = 0; i < worldPrevious.Length; i++) if (worldRenderers[i] != null) worldRenderers[i].forceRenderingOff = worldPrevious[i];
            for (int i = 0; i < firstPersonPrevious.Length; i++) if (firstPersonRenderers[i] != null) firstPersonRenderers[i].forceRenderingOff = firstPersonPrevious[i];
            overridden = false;
        }
    }
}
