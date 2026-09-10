using UnityEngine;

public class MapPin : MonoBehaviour
{
    [Header("핀 설정")]
    public string pinName = "Waypoint";
    public Color pinColor = Color.yellow;

    [Header("렌더러 참조")]
    public Renderer pinRenderer;

    public void Initialize(Color color, string name)
    {
        pinName = name;
        pinColor = color;

        if (pinRenderer != null)
        {
            pinRenderer.material.color = color;
        }
    }
}