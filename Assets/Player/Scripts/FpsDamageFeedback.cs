using UnityEngine;
using UnityEngine.UI;
namespace VoidLast.Player
{
    [DisallowMultipleComponent, RequireComponent(typeof(FpsHealth))]
    public sealed class FpsDamageFeedback : MonoBehaviour
    {
        [SerializeField] Image bloodOverlayPrefab;
        [SerializeField, Range(0.1f, 3f)] float fadeSeconds = 1.25f;
        FpsHealth health;
        FpsNetworkPlayer network;
        Camera view;
        Canvas canvas;
        Image overlay;
        float flash;
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
        void OnEnable() { health.DamageTaken += OnDamage; }
        void OnDisable()
        {
            health.DamageTaken -= OnDamage;flash=0;
            if(canvas!=null)canvas.enabled=false;
        }
        void OnDamage(float amount)
        {
            if(!LocalView) return;
            flash=Mathf.Clamp(Mathf.Max(flash,0.35f)+amount/health.Maximum,0,0.8f);
        }
        void LateUpdate()
        {
            if(canvas==null) return;
            bool visible=LocalView && !LoadingScreen.IsShowing;
            canvas.enabled=visible;
            flash=Mathf.MoveTowards(flash,0,Time.unscaledDeltaTime/Mathf.Max(0.1f,fadeSeconds));
            float lowHealth=!health.IsDead ? Mathf.Clamp01((0.25f-health.Current/health.Maximum)/0.25f)*0.2f : 0;
            overlay.color=new Color(1,1,1,visible?Mathf.Max(flash,lowHealth):0);
        }
    }
}