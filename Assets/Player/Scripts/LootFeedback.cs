using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VoidLast.Player
{
    public sealed class LootFeedback : MonoBehaviour
    {
        static LootFeedback instance;
        TMP_Text text;
        float until;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;
        public static TMP_FontAsset KoreanFont()
        {
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (font.name.Contains("NotoSansKR")) return font;
            return TMP_Settings.defaultFontAsset;
        }
        public static void Show(string message)
        {
            if (instance == null)
            {
                var root = new GameObject("Loot Feedback", typeof(LootFeedback));
                instance = root.GetComponent<LootFeedback>();
                var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 75;
                var scaler = root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920,1080);
                var label = new GameObject("Message",typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                label.transform.SetParent(root.transform,false); label.font=KoreanFont(); label.fontSize=25; label.alignment=TextAlignmentOptions.Center; label.raycastTarget=false;
                label.rectTransform.anchorMin=label.rectTransform.anchorMax=new Vector2(0.5f,0.25f); label.rectTransform.sizeDelta=new Vector2(1100,80);
                instance.text=label;
            }
            instance.text.text=message; instance.until=Time.unscaledTime+3f;
        }
        void Update() { if (text != null) text.enabled=Time.unscaledTime<until && !LoadingScreen.IsShowing; }
        void OnDestroy() { if(instance==this) instance=null; }
    }
}
