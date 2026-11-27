using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VoidLast.Player
{
    // Floor-specific items may be read anywhere; loot tables restrict acquisition per floor.
    public sealed class FpsMapItem : MonoBehaviour
    {
        public const string CurrentPlanPath = "FloorPlans/Stage2_Ship_2f";
        FpsInventory inventory;
        FpsInput input;
        GameObject panel;
        int displayedFloor;
        public bool IsOpen => panel != null && panel.activeSelf;
        void Awake() { inventory=GetComponent<FpsInventory>(); input=GetComponent<FpsInput>(); }
        void Update()
        {
            if (inventory.IsNetworkProxy) { Close(); return; }
            bool selected=inventory.IsAlive && FarmingItems.IsMap(inventory.ItemAt(inventory.SelectedIndex));
            if (!selected || FpsInput.Blocked || PauseMenu.IsOpen || LoadingScreen.IsShowing) { Close(); return; }
            if(IsOpen && displayedFloor!=FarmingItems.MapFloor(inventory.ItemAt(inventory.SelectedIndex)))Close();
            if (input.Attack) { if(IsOpen) Close(); else Open(); }
            if (input.AimPressed) Close();
        }
        void Open()
        {
            int floor=FarmingItems.MapFloor(inventory.ItemAt(inventory.SelectedIndex));
            if(floor==0)return;
            if(panel!=null && displayedFloor!=floor) { Destroy(panel);panel=null; }
            displayedFloor=floor;
            if(panel==null)
            {
                panel=new GameObject("Floor Plan",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
                panel.transform.SetParent(transform,false);
                var canvas=panel.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=65;
                var scaler=panel.GetComponent<CanvasScaler>();
                scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution=new Vector2(1920,1080);
                scaler.matchWidthOrHeight=0.5f;
                var bg=new GameObject("Map Panel",typeof(RectTransform)).AddComponent<Image>();
                bg.transform.SetParent(panel.transform,false);
                bg.color=new Color(0.035f,0.09f,0.15f,0.99f);bg.raycastTarget=false;
                bg.rectTransform.sizeDelta=new Vector2(1200,1000);
                AddLabel(bg.transform,"건물도면 · "+floor+"층",new Vector2(0,455),30);
                AddLabel(bg.transform,"좌클릭 / 우클릭: 닫기 · 슬롯 변경 시 닫힘",new Vector2(0,-462),21);
                // A shared viewport with letterboxing preserves every floor's labels and routes.
                var viewport=new GameObject("Blueprint Viewport",typeof(RectTransform)).GetComponent<RectTransform>();
                viewport.SetParent(bg.transform,false);viewport.sizeDelta=new Vector2(1120,850);
                var texture=Resources.Load<Texture2D>("FloorPlans/Stage2_Ship_"+floor+"f");
                if(texture!=null)
                {
                    var image=new GameObject("Blueprint "+floor+"F",typeof(RectTransform)).AddComponent<RawImage>();
                    image.transform.SetParent(viewport,false);image.texture=texture;image.raycastTarget=false;
                    var fit=image.gameObject.AddComponent<AspectRatioFitter>();
                    fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;
                    fit.aspectRatio=(float)texture.width/texture.height;
                }
                else AddLabel(viewport,"건물도면을 불러올 수 없습니다.",Vector2.zero,26);
            }
            panel.SetActive(true);
        }
        static void AddLabel(Transform parent,string text,Vector2 position,float size)
        {
            var label=new GameObject("Map Label",typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent,false);label.font=LootFeedback.KoreanFont();
            label.fontSize=size;label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;
            label.text=text;label.rectTransform.sizeDelta=new Vector2(1120,50);
            label.rectTransform.anchoredPosition=position;
        }
        public void Close() { if(panel!=null) panel.SetActive(false); }
        void OnDisable() => Close();
    }
}
