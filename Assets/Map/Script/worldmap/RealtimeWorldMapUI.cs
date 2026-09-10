using UnityEngine;

public class RealtimeWorldMapUI : MonoBehaviour
{
    [Header("참조")]
    public GameObject mapUIPanel;                     
    public WorldMapCameraController mapCameraController; 
    public WorldManager worldManager;
    public ShoulderCamera shoulderCam; // 🌟 ShoulderCamera 참조 연결

    [Header("미니맵 UI 연동")]
    public GameObject minimapUIPanel;

    [Header("단축키")]
    public KeyCode toggleKey = KeyCode.M;

    void Start()
    {
        if (mapUIPanel != null)
            mapUIPanel.SetActive(false);

        if (worldManager == null)
            worldManager = FindObjectOfType<WorldManager>();

        if (shoulderCam == null && Camera.main != null)
            shoulderCam = Camera.main.GetComponent<ShoulderCamera>();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleMap();
        }
    }

    public void ToggleMap()
    {
        if (mapUIPanel == null || mapCameraController == null) return;

        bool willOpen = !mapUIPanel.activeSelf;
        mapUIPanel.SetActive(willOpen);

        // 1. 미니맵 가리기/보이기
        if (minimapUIPanel != null)
            minimapUIPanel.SetActive(!willOpen);

        // 2. 월드매니저 청크 로드 모드 토글
        if (worldManager != null)
            worldManager.SetMapOpenState(willOpen);

        // 3. 🌟 커서 잠금 해제 & 메인 카메라 회전 중지 처리
        if (shoulderCam != null)
        {
            shoulderCam.inputBlocked = willOpen;
            if (willOpen)
            {
                shoulderCam.UnlockCursor();
            }
            else
            {
                shoulderCam.LockCursor();
            }
        }
        else
        {
            Cursor.lockState = willOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = willOpen;
        }

        if (willOpen)
        {
            mapCameraController.OpenMap();
        }
        else
        {
            mapCameraController.CloseMap();
        }
    }
}