using System.Collections;
using UnityEngine;

public class ChunkAppearance : MonoBehaviour
{
    [Header("등장 애니메이션 설정")]
    [Tooltip("솟아오르는 데 걸리는 시간 (초)")]
    public float duration = 0.5f;

    [Tooltip("바닥 아래 얼마나 깊은 곳에서 솟아오를지")]
    public float startYOffset = -15f;

    [Tooltip("부드러운 탄성 효과 (Ease Out Back 느낌의 커브)")]
    public AnimationCurve easeCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 2f),
        new Keyframe(1f, 1f, 0f, 0f)
    );

    // 연출 시작 함수
    public void PlaySpawnAnimation(Vector3 targetWorldPos)
    {
        StopAllCoroutines();
        StartCoroutine(AnimateRise(targetWorldPos));
    }

    private IEnumerator AnimateRise(Vector3 targetPos)
    {
        // 1. 시작 위치: 원래 위치보다 Y축으로 아래에 배치
        Vector3 startPos = new Vector3(targetPos.x, targetPos.y + startYOffset, targetPos.z);
        transform.position = startPos;

        // 약간의 스케일 팝업 효과를 주고 싶다면 활성화 (0 -> 1)
        Vector3 initialScale = transform.localScale;
        transform.localScale = new Vector3(initialScale.x * 0.8f, 0f, initialScale.z * 0.8f);

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // 커브를 통한 보간값 계산 (0 -> 1)
            float curveValue = easeCurve.Evaluate(t);

            // 위치 보간
            transform.position = Vector3.LerpUnclamped(startPos, targetPos, curveValue);

            // 스케일 보간 (자연스럽게 펴짐)
            transform.localScale = Vector3.LerpUnclamped(
                new Vector3(initialScale.x * 0.8f, 0f, initialScale.z * 0.8f),
                initialScale,
                curveValue
            );

            yield return null;
        }

        // 최종 위치 및 스케일 보정
        transform.position = targetPos;
        transform.localScale = initialScale;
    }
}