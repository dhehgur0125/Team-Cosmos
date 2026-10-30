using UnityEngine;

public class ItemDetector : MonoBehaviour
{
    [Header("탐지기 설정")]
    public float detectRadius = 15f; // 감지 반경 (15m)
    public float glowDuration = 3f;  // 발광 유지 시간
    public LayerMask itemLayer;      // 인스펙터에서 'Item' 레이어 할당 필수

    void Update()
    {
        // Q키를 누르면 탐지기 작동
        if (Input.GetKeyDown(KeyCode.Q))
        {
            ActivateDetector();
        }
    }

    private void ActivateDetector()
    {
        Debug.Log("<color=cyan>[탐지기 작동] 주변 아이템을 스캔합니다.</color>");
        
        Collider[] hitItems = Physics.OverlapSphere(transform.position, detectRadius, itemLayer);
        
        foreach (var hit in hitItems)
        {
            DetectableItem item = hit.GetComponent<DetectableItem>();
            if (item != null)
            {
                item.StartGlow(glowDuration);
            }
        }
    }
}