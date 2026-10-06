/*
    [ 코드 설명 ]

    광물 스캐너(Ore Scanner)의 장착 및 타겟 지정/해제를 담당하는 스크립트입니다.

    C키를 누름
    → 스캐너 장착 상태 토글 (isScannerEquipped)

    스캐너 장착 중 좌클릭
    → 레이캐스트를 쏴서 맞은 오브젝트가 "Ore" 태그일 때만 좌표를 기록하고 트레이서 발사

    스캐너 장착 중 우클릭
    → 지정된 좌표를 해제하며, 기존 좌표를 향해 트레이서 발사 (허공에 대고 우클릭해도 무조건 해제됨)
*/

using UnityEngine;
using Unity.Netcode;
using System;

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

    // 외부(로봇 등)에서 지정된 좌표를 구독할 수 있는 이벤트
    public static event Action<Vector3> OnOrePositionSelected;
    public static event Action OnOrePositionCleared;

    // 현재 지정된 광물 좌표 (외부 참조용 프로퍼티)
    private Vector3? currentTargetPosition = null;
    public Vector3? CurrentTargetPosition => currentTargetPosition;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
            return;

        if (playerCamera == null)
            playerCamera = Camera.main;
    }

    private void Update()
    {
        // 내 캐릭터가 아니면 입력 무시
        if (!IsOwner)
            return;

        // 1. C키를 누를 때 스캐너 장착/해제 토글
        if (Input.GetKeyDown(KeyCode.C))
        {
            isScannerEquipped = !isScannerEquipped;
            Debug.Log($"[OreScanner] 스캐너 장착 상태: {isScannerEquipped}");
        }

        // 2. 스캐너가 장착된 상태일 때만 마우스 입력 감지
        if (isScannerEquipped)
        {
            // 좌클릭: 광물("Ore" 태그) 좌표 지정
            if (Input.GetMouseButtonDown(0))
            {
                TrySelectOrePosition();
            }
            // 우클릭: 좌표 해제
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
            // 👇 맞춘 오브젝트의 태그가 "Ore"인지 확인하는 조건 추가
            if (hit.collider.CompareTag("Ore"))
            {
                currentTargetPosition = hit.point;
                
                Debug.Log($"[OreScanner] 광물(Ore) 좌표 지정 완료: {currentTargetPosition.Value}");
                
                OnOrePositionSelected?.Invoke(currentTargetPosition.Value);
                ScannerTracerRequestRpc(hit.point);
            }
            else
            {
                // Ore 태그가 아닌 엉뚱한 땅이나 나무를 맞췄을 때의 디버그
                Debug.Log($"[OreScanner] 지정 실패: 맞춘 오브젝트({hit.collider.name})가 'Ore' 태그가 아닙니다.");
            }
        }
        else
        {
            // 사거리 내에 아무것도 안 맞았을 때의 디버그
            Debug.Log("[OreScanner] 지정 실패: 사거리 내에 맞은 오브젝트가 없습니다.");
        }
    }

    private void ClearOrePosition()
    {
        if (currentTargetPosition != null)
        {
            //ScannerTracerRequestRpc(currentTargetPosition.Value);

            currentTargetPosition = null;
            
            Debug.Log("[OreScanner] 광물 좌표 지정 해제됨");
            
            OnOrePositionCleared?.Invoke();
        }
        else
        {
            // 지정된 좌표가 없는데 우클릭을 눌렀을 때의 디버그 (우클릭 무반응 원인 파악용)
            Debug.Log("[OreScanner] 해제 실패: 현재 지정된 광물 좌표가 없습니다.");
        }
    }

    // --------------------------------------------------------
    // 동기화된 트레이서 발사 로직
    // --------------------------------------------------------

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
}