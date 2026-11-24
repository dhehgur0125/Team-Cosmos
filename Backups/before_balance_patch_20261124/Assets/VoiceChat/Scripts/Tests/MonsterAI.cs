using UnityEngine;

public class MonsterAI : MonoBehaviour
{
    [Header("몬스터 이동 속도")]
    public float moveSpeed = 3.5f;
    
    private Transform targetPlayer = null;

    void Update()
    {
        // 어그로가 끌린 대상이 있다면 추적
        if (targetPlayer != null)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPlayer.position, moveSpeed * Time.deltaTime);
            transform.LookAt(targetPlayer);
        }
    }

    // 플레이어가 소리를 냈을 때 플레이어 스크립트에서 이 함수를 호출함
    public void OnHearSound(Transform playerTransform, bool isRadioSound)
    {
        // 이미 쫓고 있는 타겟이 없을 때만 어그로 변경 (또는 무전기 소리일 때만 타겟 강제 변경 등 응용 가능)
        if (targetPlayer == null)
        {
            targetPlayer = playerTransform;
            
            string soundType = isRadioSound ? "🔊 삐-빅! [무전기 송출음]" : "🗣️ [일반 발소리/말소리]";
            Debug.Log($"<color=red>몬스터가 {soundType}을(를) 감지하고 추적을 시작합니다!</color>");
        }
    }
}