/* [ 코드 설명 ] 통합 씬에서 FPS가 새로 드랍한 기존 아이템에 탐지 표시만 연결합니다. 획득·인벤토리·사용·드랍 로직이나 원본 플레이어 프리팹은 변경하지 않습니다. */
using UnityEngine;
using VoidLast.Player;

[RequireComponent(typeof(FpsItemInteractor))]
public sealed class FpsPickupDetectionBridge : MonoBehaviour
{
    FpsItemInteractor interactor;
    FpsWorldPickup registered;
    void Awake() => interactor = GetComponent<FpsItemInteractor>();
    void LateUpdate()
    {
        var dropped = interactor.LastDropped;
        if (dropped == null || dropped == registered) return;
        registered = dropped;
        if (dropped.GetComponent<DetectableItem>() == null) dropped.gameObject.AddComponent<DetectableItem>();
    }
}
