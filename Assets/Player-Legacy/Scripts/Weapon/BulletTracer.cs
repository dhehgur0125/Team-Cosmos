/*
    [ 코드 설명 ]

    총알이 날아가는 궤적을 표현하는 스크립트입니다.

    실제 총알 오브젝트를 날리는 것이 아니라,
    짧은 Line Renderer가 매우 빠르게 이동하여
    총알의 Tracer처럼 보이게 합니다.

    시작 위치
    → 총기의 MuzzlePoint

    끝 위치
    → 서버에서 계산한 명중 지점

    Start Width / End Width
    → 총알 궤적의 앞뒤 굵기를 설정합니다.

    이 스크립트는 명중 판정이나 데미지를 처리하지 않고
    총알 궤적의 시각 효과만 담당합니다.
*/

using System.Collections;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class BulletTracer : MonoBehaviour
{
    [Header("Tracer Settings")]
    [SerializeField] private float travelSpeed = 600f;
    [SerializeField] private float tracerLength = 1.5f;
    [SerializeField] private float remainTime = 0.02f;

    [Header("Tracer Width")]
    [SerializeField] private float startWidth = 0.02f;
    [SerializeField] private float endWidth = 0.01f;

    private LineRenderer lineRenderer;

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();

        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true;

        lineRenderer.startWidth = startWidth;
        lineRenderer.endWidth = endWidth;
    }

    public void Play(Vector3 start, Vector3 end)
    {
        StartCoroutine(PlayTracer(start, end));
    }

    private IEnumerator PlayTracer(Vector3 start, Vector3 end)
    {
        float distance = Vector3.Distance(start, end);

        if (distance <= 0.001f)
        {
            Destroy(gameObject);
            yield break;
        }

        float traveled = 0f;

        while (traveled < distance)
        {
            traveled += travelSpeed * Time.deltaTime;

            float headDistance =
                Mathf.Min(traveled, distance);

            float tailDistance =
                Mathf.Max(
                    0f,
                    headDistance - tracerLength
                );

            Vector3 head =
                Vector3.Lerp(
                    start,
                    end,
                    headDistance / distance
                );

            Vector3 tail =
                Vector3.Lerp(
                    start,
                    end,
                    tailDistance / distance
                );

            lineRenderer.SetPosition(0, tail);
            lineRenderer.SetPosition(1, head);

            yield return null;
        }

        yield return new WaitForSeconds(remainTime);

        Destroy(gameObject);
    }
}