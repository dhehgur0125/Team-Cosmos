using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
namespace VoidLast.Player
{
    [DisallowMultipleComponent, RequireComponent(typeof(FpsHealth))]
    public sealed class FpsDamageFeedback : MonoBehaviour
    {
        [SerializeField] Image bloodOverlayPrefab;
        [SerializeField, Range(0.1f, 3f)] float fadeSeconds = 1.25f;
        [Header("피격 카메라 흔들림")]
        [SerializeField] bool shakeEnabled = true;
        [SerializeField, Range(0f, 2f)] float shakeIntensity = 2f;
        [SerializeField, Range(0.1f, 0.8f)] float shakeDuration = 0.6f;
        [SerializeField, Range(0f, 3f)] float shakeAngle = 3f;
        [SerializeField, Range(0f, 0.04f)] float shakeDistance = 0.04f;
        FpsHealth health;
        FpsNetworkPlayer network;
        Camera view;
        Canvas canvas;
        Image overlay;
        float flash, shakeElapsed, shakeStrength;
        int hitSequence;
        Vector3 shakePosition, savedPosition;
        Quaternion shakeRotation = Quaternion.identity, savedRotation;
        bool cameraOffsetApplied;
        public float VisibleIntensity => overlay != null ? overlay.color.a : 0;
        bool LocalView => (network == null || !network.IsSpawned || network.IsOwner) && view != null && view.isActiveAndEnabled;
        void Awake()
        {
            health=GetComponent<FpsHealth>();
            network=GetComponent<FpsNetworkPlayer>();
            view=GetComponentInChildren<Camera>(true);
            if(bloodOverlayPrefab==null) return;
            var go=new GameObject("Damage Feedback",typeof(RectTransform),typeof(Canvas));
            go.transform.SetParent(transform,false);
            canvas=go.GetComponent<Canvas>();
            canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder=90;
            overlay=Instantiate(bloodOverlayPrefab,go.transform);
            overlay.name="Blood Edges";
            overlay.raycastTarget=false;
            var r=overlay.rectTransform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
            overlay.color=new Color(1,1,1,0);
            canvas.enabled=false;
        }
        void OnEnable()
        {
            health.DamageTaken += OnDamage;
            RenderPipelineManager.beginCameraRendering += BeginSrp;
            RenderPipelineManager.endCameraRendering += EndSrp;
            Camera.onPreCull += BeginBuiltIn;
            Camera.onPostRender += EndBuiltIn;
        }
        void OnDisable()
        {
            health.DamageTaken -= OnDamage;
            RenderPipelineManager.beginCameraRendering -= BeginSrp;
            RenderPipelineManager.endCameraRendering -= EndSrp;
            Camera.onPreCull -= BeginBuiltIn;
            Camera.onPostRender -= EndBuiltIn;
            flash=0; ClearShake();
            if(canvas!=null)canvas.enabled=false;
        }
        void OnDamage(float amount)
        {
            if(!LocalView || amount <= 0) return;
            flash=Mathf.Clamp(Mathf.Max(flash,0.35f)+amount/health.Maximum,0,0.8f);
            if (!shakeEnabled || LoadingScreen.IsShowing || FpsInput.Blocked || Time.timeScale <= 0 || health.IsDead) return;
            // Restart a bounded impulse: repeated hits never accumulate camera displacement.
            shakeStrength = Mathf.Min(1f, Mathf.Max(shakeStrength, 0.65f + amount / health.Maximum));
            shakeElapsed = 0;
            hitSequence++;
        }
        void LateUpdate()
        {
            // Safety restore if rendering was interrupted. Never carry a visual offset into gameplay.
            RestoreCamera();
            bool visible=LocalView && !LoadingScreen.IsShowing;
            TickShake(Time.unscaledDeltaTime, visible && !health.IsDead && !FpsInput.Blocked && Time.timeScale > 0);
            if(canvas==null) return;
            canvas.enabled=visible;
            flash=Mathf.MoveTowards(flash,0,Time.unscaledDeltaTime/Mathf.Max(0.1f,fadeSeconds));
            float lowHealth=!health.IsDead ? Mathf.Clamp01((0.25f-health.Current/health.Maximum)/0.25f)*0.2f : 0;
            overlay.color=new Color(1,1,1,visible?Mathf.Max(flash,lowHealth):0);
        }
        void TickShake(float delta, bool allowed)
        {
            if (!allowed || !shakeEnabled || shakeIntensity <= 0 || shakeStrength <= 0) { ClearShake(); return; }
            shakeElapsed += Mathf.Max(0, delta);
            float t = Mathf.Clamp01(shakeElapsed / Mathf.Max(0.1f, shakeDuration));
            if (t >= 1) { ClearShake(); return; }
            float envelope = (1-t)*shakeStrength*Mathf.Clamp(shakeIntensity,0,2);
            float phase = shakeElapsed * 12f * Mathf.PI * 2f;
            float side = (hitSequence & 1) == 0 ? 1f : -1f;
            shakePosition = new Vector3(Mathf.Sin(phase)*side, Mathf.Sin(phase*0.73f)*0.6f, 0) * shakeDistance * envelope;
            shakeRotation = Quaternion.Euler(
                -Mathf.Cos(phase*0.65f)*shakeAngle*envelope,
                Mathf.Sin(phase*0.81f)*shakeAngle*0.35f*envelope,
                Mathf.Cos(phase)*side*shakeAngle*0.65f*envelope);
        }
        void ClearShake()
        {
            RestoreCamera();
            shakeStrength=0; shakeElapsed=0;
            shakePosition=Vector3.zero; shakeRotation=Quaternion.identity;
        }
        void BeginSrp(ScriptableRenderContext context, Camera camera) => ApplyCamera(camera);
        void EndSrp(ScriptableRenderContext context, Camera camera) { if(camera==view) RestoreCamera(); }
        void BeginBuiltIn(Camera camera) { if(GraphicsSettings.currentRenderPipeline==null) ApplyCamera(camera); }
        void EndBuiltIn(Camera camera) { if(GraphicsSettings.currentRenderPipeline==null && camera==view) RestoreCamera(); }
        void ApplyCamera(Camera camera)
        {
            if(camera!=view) return;
            RestoreCamera();
            if(!LocalView || !shakeEnabled || shakeStrength<=0 || health.IsDead || LoadingScreen.IsShowing || FpsInput.Blocked || Time.timeScale<=0) return;
            savedPosition=view.transform.localPosition;
            savedRotation=view.transform.localRotation;
            view.transform.localPosition=savedPosition+shakePosition;
            view.transform.localRotation=savedRotation*shakeRotation;
            cameraOffsetApplied=true;
        }
        void RestoreCamera()
        {
            if(!cameraOffsetApplied) return;
            if(view!=null) { view.transform.localPosition=savedPosition; view.transform.localRotation=savedRotation; }
            cameraOffsetApplied=false;
        }
    }
}
