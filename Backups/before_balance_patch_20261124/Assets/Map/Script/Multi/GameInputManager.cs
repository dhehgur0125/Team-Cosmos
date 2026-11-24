using UnityEngine;

public class GameInputManager : MonoBehaviour
{
    public static GameInputManager Instance { get; private set; }

    // 게임 조작 입력이 허용되는지 여부
    public bool GameInputEnabled { get; private set; } = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    // 게임 조작 입력 허용/차단
    public void SetGameInputEnabled(bool enabled)
    {
        GameInputEnabled = enabled;
    }
}