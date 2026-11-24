/* [ 코드 설명 ] 기존 발사·피격 이벤트로 실탄 화염과 표면별 피격 입자를 재생합니다. 조준 시 FPS 화염 크기를 줄이고 하나의 짧은 총구 조명을 렌더 카메라의 실제 총구 위치에 맞춥니다. 사격 동기화·피해·탄흔·손전등은 변경하지 않습니다. */
using UnityEngine;
using UnityEngine.Rendering;

namespace VoidLast.Player
{
    public sealed class FpsPistolEffects : MonoBehaviour
    {
        [SerializeField] ParticleSystem localFlash;
        [SerializeField] ParticleSystem worldFlash;
        [SerializeField] Light muzzleLight;
        [SerializeField] ParticleSystem impactPrefab;
        [SerializeField] ParticleSystem dustImpactPrefab;
        [Tooltip("Metallic 값/이름으로 구분하기 어려운 금속 머티리얼만 추가합니다. 원본 벽이나 씬은 수정할 필요가 없습니다.")]
        [SerializeField] Material[] metalMaterials = new Material[0];
        [SerializeField, Min(0.005f)] float flashSize = 0.2f;
        [SerializeField, Range(0.3f, 1)] float aimedFlashSizeMultiplier = 0.6f;
        [SerializeField, Range(0.015f, 0.12f)] float flashDuration = 0.065f;
        [SerializeField, Range(0.005f, 0.05f)] float lightDuration = 0.025f;
        [SerializeField, Min(0)] float lightIntensity = 0.35f;
        [SerializeField, Min(0.1f)] float lightRange = 1.25f;
        [SerializeField, Min(0.1f)] float impactScale = 1;
        FpsPistol pistol;
        FpsEquipment equipment;
        FpsMotor motor;
        float remaining, lightRemaining;
        int firedFrame = -1;
        Camera localCamera;
        Vector3 lightPositionBeforeRender;
        bool lightOverridden;
        public bool IsFlashing => remaining > 0;
        public ParticleSystem LocalFlash => localFlash;
        public ParticleSystem WorldFlash => worldFlash;
        public ParticleSystem LastImpact { get; private set; }

        void Awake()
        {
            pistol = GetComponent<FpsPistol>(); equipment = GetComponent<FpsEquipment>(); motor = GetComponent<FpsMotor>();
            if (localFlash != null) localCamera = localFlash.GetComponentInParent<Camera>(true);
        }
        void OnEnable()
        {
            pistol.Shot += Fire; pistol.Hit += Impact; pistol.Changed += StateChanged;
            equipment.SlotChanged += Clear; Clear();
            RenderPipelineManager.beginCameraRendering += BeginSrp;
            RenderPipelineManager.endCameraRendering += EndSrp;
            Camera.onPreCull += BeginCamera; Camera.onPostRender += EndCamera;
        }
        void OnDisable()
        {
            pistol.Shot -= Fire; pistol.Hit -= Impact; pistol.Changed -= StateChanged;
            equipment.SlotChanged -= Clear; Clear();
            RenderPipelineManager.beginCameraRendering -= BeginSrp;
            RenderPipelineManager.endCameraRendering -= EndSrp;
            Camera.onPreCull -= BeginCamera; Camera.onPostRender -= EndCamera;
            RestoreLight();
        }
        void Update()
        {
            if (motor.IsDead) { Clear(); return; }
            if (firedFrame == Time.frameCount) return; // Render at least the firing frame, even at a low frame rate.
            remaining = Mathf.Max(0, remaining - Time.deltaTime);
            lightRemaining = Mathf.Max(0, lightRemaining - Time.deltaTime);
            if (muzzleLight != null)
            {
                muzzleLight.enabled = lightRemaining > 0 && lightIntensity > 0;
                muzzleLight.intensity = lightIntensity * Mathf.Clamp01(lightRemaining / Mathf.Max(0.005f, lightDuration));
            }
        }
        void Fire()
        {
            remaining = Mathf.Max(0.015f, flashDuration);
            lightRemaining = Mathf.Max(0.005f, lightDuration);
            firedFrame = Time.frameCount;
            Emit(localFlash, equipment.IsAiming ? aimedFlashSizeMultiplier : 1); Emit(worldFlash, 1);
            if (muzzleLight != null) { muzzleLight.range = lightRange; muzzleLight.intensity = lightIntensity; muzzleLight.enabled = lightIntensity > 0; }
        }
        void Emit(ParticleSystem flash, float sizeMultiplier)
        {
            if (flash == null || !flash.gameObject.activeInHierarchy) return;
            flash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            flash.Play();
            flash.Emit(new ParticleSystem.EmitParams { startSize = flashSize * sizeMultiplier * Random.Range(0.92f, 1.08f), startLifetime = remaining, rotation = Random.Range(0, 360) }, 1);
        }
        void BeginSrp(ScriptableRenderContext context, Camera camera) => BeginCamera(camera);
        void EndSrp(ScriptableRenderContext context, Camera camera) => RestoreLight();
        void EndCamera(Camera camera) => RestoreLight();
        void BeginCamera(Camera camera)
        {
            RestoreLight();
            if (muzzleLight == null || !muzzleLight.enabled || worldFlash == null || localFlash == null) return;
            // The light has its own child Transform, so moving it never moves the world gun/particles.
            if (muzzleLight.transform == worldFlash.transform || muzzleLight.transform == localFlash.transform) return;
            var source = camera == localCamera ? localFlash : worldFlash;
            lightPositionBeforeRender = muzzleLight.transform.position;
            muzzleLight.transform.position = source.transform.position;
            lightOverridden = true;
        }
        void RestoreLight()
        {
            if (lightOverridden && muzzleLight != null) muzzleLight.transform.position = lightPositionBeforeRender;
            lightOverridden = false;
        }
        void Impact(RaycastHit hit)
        {
            if (hit.collider == null || hit.collider.GetComponentInParent<IFpsDamageTarget>() != null || hit.collider.GetComponentInParent<FpsHealth>() != null || hit.collider.GetComponentInParent<Animator>() != null) return;
            var prefab = IsMetal(hit) ? impactPrefab : dustImpactPrefab;
            if (prefab == null) return;
            LastImpact = Instantiate(prefab, hit.point + hit.normal * 0.003f, Quaternion.LookRotation(hit.normal));
            LastImpact.transform.localScale = Vector3.one * impactScale;
            LastImpact.Play(true); // Non-looping prefab destroys itself when its last particle expires.
        }
        bool IsMetal(RaycastHit hit)
        {
            if (hit.collider.sharedMaterial != null && MetalName(hit.collider.sharedMaterial.name)) return true;
            var surface = hit.collider.GetComponent<Renderer>();
            if (surface == null) surface = hit.collider.GetComponentInParent<Renderer>();
            if (surface == null) surface = hit.collider.GetComponentInChildren<Renderer>();
            if (surface == null) return false;
            var materials = surface.sharedMaterials;
            int index = 0;
            // MeshCollider triangle indices identify the hit submesh, rather than any metal material on the object.
            if (hit.collider is MeshCollider meshCollider && meshCollider.sharedMesh != null && hit.triangleIndex >= 0)
            {
                var mesh = meshCollider.sharedMesh;
                int triangle = hit.triangleIndex * 3;
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    var submesh = mesh.GetSubMesh(i);
                    if (triangle >= submesh.indexStart && triangle < submesh.indexStart + submesh.indexCount) { index = i; break; }
                }
            }
            if (materials.Length == 0) return false;
            var material = materials[Mathf.Min(index, materials.Length - 1)];
            if (material == null) return false;
            foreach (var metal in metalMaterials) if (metal != null && metal == material) return true;
            return material.HasProperty("_Metallic") && material.GetFloat("_Metallic") >= 0.5f || MetalName(material.name);
        }
        static bool MetalName(string value)
        {
            value = value.ToLowerInvariant();
            return value.Contains("metal") || value.Contains("steel") || value.Contains("iron") || value.Contains("alumin") || value.Contains("금속") || value.Contains("철판");
        }
        void StateChanged() { if (pistol.IsReloading) Clear(); }
        void Clear()
        {
            RestoreLight();
            remaining = lightRemaining = 0;
            if (localFlash != null) localFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (worldFlash != null) worldFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (muzzleLight != null) muzzleLight.enabled = false;
        }
    }
}
