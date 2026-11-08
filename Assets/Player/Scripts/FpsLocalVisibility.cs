/* [ 코드 설명 ] 자신의 FPS 카메라에는 1인칭 장비·장갑을, 다른 카메라에는 전신과 손의 장비를 표시합니다. 카메라별로 표시가 달라지는 메시를 GPU 렌더 캐시에서 제외하고, 렌더 콜백의 숨김 상태를 즉시 복원합니다. 모델·본·Animator와 원본 머티리얼은 유지합니다. */
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using System.Collections.Generic;

namespace VoidLast.Player
{
    public sealed class FpsLocalVisibility : MonoBehaviour
    {
        [SerializeField] Camera localCamera;
        [SerializeField, FormerlySerializedAs("headRenderers")] Renderer[] worldRenderers;
        [SerializeField] Renderer[] firstPersonRenderers;
        bool[] worldPrevious, firstPersonPrevious;
        bool overridden;
        static readonly int PerCameraVisibility = Shader.PropertyToID("_FpsPerCameraVisibility");
        MaterialPropertyBlock perCameraProperties;
        /// <summary>실행 중에 만들어진 렌더러(예: 무전기 모델)를 1인칭/전신 표시 규칙에 추가한다.</summary>
        public void AddRenderers(Renderer[] world, Renderer[] firstPerson)
        {
            Restore();
            worldRenderers = Concat(worldRenderers, world);
            firstPersonRenderers = Concat(firstPersonRenderers, firstPerson);
            PreparePerCameraMeshes(worldRenderers);
            PreparePerCameraMeshes(firstPersonRenderers);
            worldPrevious = new bool[worldRenderers.Length];
            firstPersonPrevious = new bool[firstPersonRenderers.Length];
        }
        static Renderer[] Concat(Renderer[] a, Renderer[] b)
        {
            // A nested weapon/flashlight may already be in the prefab list. Duplicates corrupt Restore's saved state.
            var result = new List<Renderer>();
            foreach (var list in new[] { a, b })
                if (list != null) foreach (var renderer in list)
                    if (renderer != null && !result.Contains(renderer)) result.Add(renderer);
            return result.ToArray();
        }
        void PreparePerCameraMeshes(Renderer[] renderers)
        {
            // GPU Resident Drawer caches MeshRenderer visibility before camera callbacks.
            // A per-renderer block keeps these few held-item meshes on the regular rendering path,
            // including models created later by AddRenderers. This unused shader value changes no appearance.
            // Existing emission/tint blocks already opt out; never replace them or instantiate materials.
            foreach (var renderer in renderers)
            {
                if (!(renderer is MeshRenderer) || renderer.HasPropertyBlock()) continue;
                if (perCameraProperties == null) perCameraProperties = new MaterialPropertyBlock();
                perCameraProperties.Clear();
                perCameraProperties.SetFloat(PerCameraVisibility, 1);
                renderer.SetPropertyBlock(perCameraProperties);
            }
        }
        void OnEnable()
        {
            worldRenderers = Concat(worldRenderers, null);
            firstPersonRenderers = Concat(firstPersonRenderers, null);
            PreparePerCameraMeshes(worldRenderers);
            PreparePerCameraMeshes(firstPersonRenderers);
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
