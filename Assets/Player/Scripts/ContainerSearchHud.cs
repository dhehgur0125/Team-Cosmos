using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace VoidLast.Player
{
    public sealed class ContainerSearchHud : MonoBehaviour
    {
        FpsItemInteractor interactor;
        FpsInventory inventory;
        GameObject root;
        TMP_Text label;
        Image fill;
        void Start()
        {
            interactor=GetComponent<FpsItemInteractor>();inventory=GetComponent<FpsInventory>();
            root=new GameObject("Container Search HUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            root.transform.SetParent(transform,false);var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=45;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=0.5f;
            label=new GameObject("Search Prompt",typeof(RectTransform)).AddComponent<TextMeshProUGUI>();label.transform.SetParent(root.transform,false);label.font=LootFeedback.KoreanFont();label.fontSize=23;label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;
            label.rectTransform.anchorMin=label.rectTransform.anchorMax=new Vector2(0.5f,0.3f);label.rectTransform.sizeDelta=new Vector2(950,60);
            var bg=new GameObject("Progress Background",typeof(RectTransform)).AddComponent<Image>();bg.transform.SetParent(root.transform,false);bg.color=new Color(0.08f,0.12f,0.15f,0.9f);bg.raycastTarget=false;
            bg.rectTransform.anchorMin=bg.rectTransform.anchorMax=new Vector2(0.5f,0.3f);bg.rectTransform.anchoredPosition=new Vector2(0,-36);bg.rectTransform.sizeDelta=new Vector2(300,8);
            fill=new GameObject("Progress",typeof(RectTransform)).AddComponent<Image>();fill.transform.SetParent(bg.transform,false);fill.color=new Color(0.3f,0.9f,0.85f);fill.raycastTarget=false;
            fill.rectTransform.anchorMin=Vector2.zero;fill.rectTransform.anchorMax=Vector2.one;fill.rectTransform.offsetMin=fill.rectTransform.offsetMax=Vector2.zero;
            root.SetActive(false);
        }
        void Update()
        {
            if(root==null)return;
            bool visible=interactor!=null&&interactor.isActiveAndEnabled&&inventory!=null&&!inventory.IsNetworkProxy&&inventory.IsAlive&&interactor.ContainerTarget!=null&&!FpsInput.Blocked&&!PauseMenu.IsOpen&&!LoadingScreen.IsShowing;
            root.SetActive(visible);if(!visible)return;
            label.text=inventory.IsFull?"인벤토리가 가득 찼습니다 · 칸을 비우고 다시 시도해 주세요":!interactor.ContainerTarget.seedReady?"상자 정보 동기화 중…":interactor.ContainerHoldCompleted?"수색 완료 · F키를 놓아 주세요":"F 길게 누르기 · 상자 수색 ("+interactor.ContainerSearchSeconds.ToString("0.0")+"초)";
            fill.rectTransform.anchorMax=new Vector2(interactor.ContainerSearchProgress,1);
        }
        void OnDisable() { if(root!=null)root.SetActive(false); }
    }
}
