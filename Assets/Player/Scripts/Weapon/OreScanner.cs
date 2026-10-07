/*
    [ 코드 설명 ]

    광물 스캐너(Ore Scanner)의 장착 및 타겟 지정/해제를 담당하는 스크립트입니다.

    C키를 누름
    → 스캐너 장착 상태 토글 (isScannerEquipped)

    스캐너 장착 중 좌클릭
    → 레이캐스트를 쏴서 맞은 오브젝트가 "Ore" 태그일 때만 좌표를 기록, 트레이서 발사, 그리고 관제탑(TaskManager)에 작업 하달

    스캐너 장착 중 우클릭
    → 로컬 스캐너의 지정 좌표 해제
*/

using UnityEngine;
using Unity.Netcode;
using System;
using System.Collections.Generic;

public class OreScanner : NetworkBehaviour
{
    [Header("Scanner Settings")]
    [Tooltip("지형 및 광물 레이어 마스크")]
    [SerializeField] private LayerMask targetLayer = ~0;

    [Tooltip("레이캐스트 최대 거리")]
    [SerializeField] private float maxScanDistance = 100f;

    [Header("Tracer")]
    [Tooltip("스캐너 레이저(트레이서)가 발사될 위치 (예: 스캐너 총구)")]
    [SerializeField] private Transform scannerMuzzle;
    
    [Tooltip("발사될 트레이서 프리팹")]
    [SerializeField] private GameObject tracerPrefab;

    [Header("Camera Reference")]
    private Camera playerCamera;

    // 스캐너 장착 상태 플래그
    private bool isScannerEquipped = false;

    // 로컬 UI 등에서 구독할 수 있는 이벤트 (로봇은 더 이상 이 이벤트를 직접 듣지 않음)
    public static event Action<Vector3> OnOrePositionSelected;
    public static event Action OnOrePositionCleared;

    private Vector3? currentTargetPosition = null;
    public Vector3? CurrentTargetPosition => currentTargetPosition;

    private List<Vector3> localTaskQueue = new List<Vector3>();
    private List<GameObject> visualMarkers = new List<GameObject>();

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
            return;

        if (playerCamera == null)
            playerCamera = Camera.main;
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        if (Input.GetKeyDown(KeyCode.C))
        {
            isScannerEquipped = !isScannerEquipped;
            Debug.Log($"[OreScanner] 스캐너 장착 상태: {isScannerEquipped}");
        }

        if (isScannerEquipped)
        {
            if (Input.GetMouseButtonDown(0))
            {
                TrySelectOrePosition();
            }
            else if (Input.GetMouseButtonDown(1))
            {
                ClearOrePosition();
            }
        }
    }

    private void TrySelectOrePosition()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;

        if (playerCamera == null)
            return;

        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, maxScanDistance, targetLayer, QueryTriggerInteraction.Ignore))
        {
            OreNode ore = hit.collider.GetComponentInParent<OreNode>();
                if (ore == null) return;

                Vector3 exactPos = ore.transform.position;

                // 이미 대기열에 들어간 광물인지 중복 검사
                if (!localTaskQueue.Contains(exactPos))
                {
                    localTaskQueue.Add(exactPos);
                    
                    // 시각적 피드백: 광물 머리 위에 임시 마커(빨간 구슬) 띄우기
                    GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    marker.transform.position = exactPos + Vector3.up * 20f; 
                    marker.transform.localScale = Vector3.one * 0.7f;
                    marker.GetComponent<Collider>().enabled = false;
                    marker.GetComponent<Renderer>().material.color = Color.red;
                    visualMarkers.Add(marker);

                    Debug.Log($"[OreScanner] 광물 예약 추가! 현재 대기열: {localTaskQueue.Count}개");
                    
                    ScannerTracerRequestRpc(hit.point);
                    SendTaskToServerRpc(exactPos);
                }
                else
                {
                    Debug.Log("[OreScanner] 이미 예약 대기열에 있는 광물입니다.");
                }
        }
        else
        {
            Debug.Log("[OreScanner] 지정 실패: 사거리 내에 맞은 오브젝트가 없습니다.");
        }
    }

    private void ClearOrePosition()
    {
        if (localTaskQueue.Count > 0)
        {
            int lastIndex = localTaskQueue.Count - 1;
            Vector3 targetToCancel = localTaskQueue[lastIndex];
            
            CancelTaskOnServerRpc(targetToCancel);
            
            // 시각적 마커 삭제 및 리스트에서 제거
            Destroy(visualMarkers[lastIndex]);
            visualMarkers.RemoveAt(lastIndex);
            localTaskQueue.RemoveAt(lastIndex);
            
            Debug.Log($"[OreScanner] 마지막 예약 취소됨. 남은 예약: {localTaskQueue.Count}개");
            OnOrePositionCleared?.Invoke();
        }
        else
        {
            Debug.Log("[OreScanner] 취소할 예약이 없습니다.");
        }
    }

    // --------------------------------------------------------
    // 동기화 및 서버 요청 로직
    // --------------------------------------------------------

    // 🌟 [추가됨] 클라이언트가 스캔한 좌표를 서버의 관제탑 작업 대기열에 넣으라고 요청함
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SendTaskToServerRpc(Vector3 targetPos)
    {
        if (RobotTaskManager.Instance != null)
        {
            RobotTaskManager.Instance.AddMiningTask(targetPos);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void ScannerTracerRequestRpc(Vector3 targetPoint)
    {
        if (scannerMuzzle == null)
            return;

        PlayTracerRpc(scannerMuzzle.position, targetPoint);
    }

    [Rpc(SendTo.Everyone)]
    private void PlayTracerRpc(Vector3 start, Vector3 end)
    {
        if (tracerPrefab == null)
            return;

        GameObject tracerObject = Instantiate(
            tracerPrefab,
            Vector3.zero,
            Quaternion.identity
        );

        BulletTracer tracer = tracerObject.GetComponent<BulletTracer>();
        if (tracer != null)
        {
            tracer.Play(start, end);
        }
    }
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void CancelTaskOnServerRpc(Vector3 targetPos)
    {
        if (RobotTaskManager.Instance != null)
        {
            RobotTaskManager.Instance.CancelTask(targetPos);
        }
    }

    // ========================================================
    // 🌟 [수정됨] 채굴 완료 시 빨간 구슬 마커 삭제 로직
    // ========================================================

    public static void ClearMarkerOnAllClients(Vector3 destroyedPos)
    {
        OreScanner[] allScanners = FindObjectsOfType<OreScanner>();
        
        foreach (OreScanner scanner in allScanners)
        {
            // 🌟 break; 삭제! 씬에 있는 '모든' 스캐너에게 신호를 쏴야 
            // 각자 자기 화면(IsOwner)에 있는 마커를 정상적으로 지울 수 있습니다.
            if (scanner.IsSpawned)
            {
                scanner.RemoveCompletedMarkerClientRpc(destroyedPos);
            }
        }
    }

    [Rpc(SendTo.Everyone)]
    private void RemoveCompletedMarkerClientRpc(Vector3 destroyedPos)
    {
        if (!IsOwner) return;

        // 리스트를 거꾸로 뒤지면서, 파괴된 광물의 좌표와 일치하는 마커를 찾아 삭제
        for (int i = localTaskQueue.Count - 1; i >= 0; i--)
        {
            float dist = Vector3.Distance(localTaskQueue[i], destroyedPos);
            
            // 🌟 혹시 모를 좌표 오차를 대비해 1.0f -> 2.5f 로 넉넉하게 변경
            if (dist < 2.5f)
            {
                if (visualMarkers[i] != null)
                {
                    Destroy(visualMarkers[i]); // 빨간 구슬 오브젝트 파괴
                }
                
                visualMarkers.RemoveAt(i);
                localTaskQueue.RemoveAt(i);
                
                Debug.Log($"[OreScanner] 🔴 로봇이 광물을 부숴서 마커를 정상 삭제했습니다! (남은 대기열: {localTaskQueue.Count}개)");
                return; // 하나 지우고 함수 종료
            }
        }
    }
}