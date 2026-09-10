using System.Collections.Generic;
using UnityEngine;

public class MapPinManager : MonoBehaviour
{
    public static MapPinManager Instance { get; private set; }

    [Header("프리팹 및 설정")]
    public GameObject pinPrefab;           // 바닥에 세워질 핀 프리팹
    public int maxPins = 8;                // 설치 가능한 최대 핀 개수
    public LayerMask terrainLayer = ~0;    // 지형 레이어
    public float pinRemoveRadius = 6.0f;   // 우클릭 시 기존 핀 삭제로 인식할 거리

    [Header("참조")]
    public Camera worldMapCamera;          // WorldMap Camera 참조

    private List<MapPin> activePins = new List<MapPin>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    /// <summary>
    /// 월드맵이 켜진 상태에서 마우스 화면 클릭 좌표를 받아 핀을 설치하거나 삭제합니다.
    /// </summary>
    public void HandleMapClick(Vector2 screenMousePos)
    {
        if (worldMapCamera == null || pinPrefab == null) return;

        // 월드맵 카메라에서 마우스 위치로 레이 발사
        Ray ray = worldMapCamera.ScreenPointToRay(screenMousePos);
        if (Physics.Raycast(ray, out RaycastHit hit, 5000f, terrainLayer, QueryTriggerInteraction.Ignore))
        {
            Vector3 hitPoint = hit.point;

            // 1. 이미 클릭 지점 근처에 핀이 있다면 삭제
            MapPin nearestPin = GetNearestPin(hitPoint, pinRemoveRadius);
            if (nearestPin != null)
            {
                RemovePin(nearestPin);
                return;
            }

            // 2. 최대 개수 초과 시 가장 오래된 핀 제거
            if (activePins.Count >= maxPins)
            {
                RemovePin(activePins[0]);
            }

            // 3. 새 핀 생성
            SpawnPin(hitPoint);
        }
    }

    void SpawnPin(Vector3 position)
    {
        GameObject obj = Instantiate(pinPrefab, position, Quaternion.identity);
        MapPin pin = obj.GetComponent<MapPin>();
        if (pin == null) pin = obj.AddComponent<MapPin>();

        pin.Initialize(Color.yellow, $"Pin #{activePins.Count + 1}");
        activePins.Add(pin);
    }

    public void RemovePin(MapPin pin)
    {
        if (activePins.Contains(pin))
        {
            activePins.Remove(pin);
            Destroy(pin.gameObject);
        }
    }

    MapPin GetNearestPin(Vector3 pos, float maxDist)
    {
        MapPin closest = null;
        float minDst = maxDist;

        foreach (var pin in activePins)
        {
            if (pin == null) continue;
            float dst = Vector3.Distance(new Vector3(pos.x, 0, pos.z), new Vector3(pin.transform.position.x, 0, pin.transform.position.z));
            if (dst < minDst)
            {
                minDst = dst;
                closest = pin;
            }
        }
        return closest;
    }

    public List<MapPin> GetActivePins() => activePins;
}