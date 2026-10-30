using UnityEngine;
using System.Collections;

public class DetectableItem : MonoBehaviour
{
    private Renderer itemRenderer;
    private Material itemMaterial;
    private Color originalEmission;

    [Header("발광 색상")]
    [ColorUsage(true, true)] // HDR 색상 활성화 (URP에서 강하게 빛나게 함)
    public Color glowColor = Color.yellow * 2f; 

    void Start()
    {
        itemRenderer = GetComponent<Renderer>();
        if (itemRenderer != null)
        {
            itemMaterial = itemRenderer.material;
            if (itemMaterial.HasProperty("_EmissionColor"))
            {
                originalEmission = itemMaterial.GetColor("_EmissionColor");
            }
        }
    }

    public void StartGlow(float duration)
    {
        StopAllCoroutines(); // 이미 켜져있다면 시간 초기화
        StartCoroutine(GlowCoroutine(duration));
    }

    private IEnumerator GlowCoroutine(float duration)
    {
        if (itemMaterial != null)
        {
            itemMaterial.EnableKeyword("_EMISSION");
            itemMaterial.SetColor("_EmissionColor", glowColor);
        }

        yield return new WaitForSeconds(duration);

        if (itemMaterial != null)
        {
            itemMaterial.SetColor("_EmissionColor", originalEmission);
        }
    }
}