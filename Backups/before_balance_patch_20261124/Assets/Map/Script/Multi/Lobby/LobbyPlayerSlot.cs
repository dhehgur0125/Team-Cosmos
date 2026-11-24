using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>대기방의 플레이어 카드 한 칸.</summary>
public class LobbyPlayerSlot : MonoBehaviour
{
    public Image frame;
    public Image avatar;
    public TMP_Text initialText;
    public TMP_Text nameText;
    public TMP_Text roleText;
    public TMP_Text statusText;
    public Image statusBar;
    public Button kickButton;
    [Header("음성 채팅 표시 (선택)")]
    [Tooltip("말하는 동안 아바타 둘레에 켜지는 링")]
    public Image speakingRing;
    [Tooltip("음성 연결 상태 (말하는 중 / 마이크 꺼짐)")]
    public TMP_Text voiceText;

    Action _onKick;

    void Awake()
    {
        kickButton.onClick.AddListener(() => _onKick?.Invoke());
    }

    public void SetPlayer(string playerName, bool isHost, bool isReady, bool isMe, bool canKick, Action onKick)
    {
        gameObject.SetActive(true);
        _onKick = onKick;
        frame.color = isMe ? LobbyStyle.AccentDark : LobbyStyle.PanelLight;
        avatar.color = isHost ? LobbyStyle.Accent : new Color(0.25f, 0.3f, 0.36f);
        initialText.text = string.IsNullOrEmpty(playerName) ? "?" : playerName.Substring(0, 1);
        nameText.text = playerName + (isMe ? "  (나)" : "");
        nameText.color = LobbyStyle.Text;
        roleText.text = isHost ? "방장" : "대원";
        roleText.color = isHost ? LobbyStyle.Accent : LobbyStyle.TextDim;
        statusText.text = isReady ? "준비 완료" : "대기 중";
        statusText.color = isReady ? LobbyStyle.Ready : LobbyStyle.TextDim;
        statusBar.color = isReady ? LobbyStyle.Ready : new Color(0.3f, 0.33f, 0.38f);
        kickButton.gameObject.SetActive(canKick);
    }

    /// <summary>고른 고유 아이템 표시 (역할 글자 옆). 빈 글자면 아직 안 고름</summary>
    public void SetUniqueItem(string itemName)
    {
        if (roleText == null) return;
        string role = roleText.text;
        int cut = role.IndexOf("  ·  ", StringComparison.Ordinal);
        if (cut >= 0) role = role.Substring(0, cut);
        roleText.text = role + "  ·  " + (string.IsNullOrEmpty(itemName) ? "<color=#8A949E>아이템 고르는 중</color>" : "<color=#7FD6F0>" + itemName + "</color>");
        roleText.richText = true;
    }

    /// <summary>음성 채팅 상태 표시</summary>
    public void SetVoice(bool connected, bool speaking, bool muted)
    {
        if (speakingRing != null)
        {
            if (speakingRing.gameObject.activeSelf != speaking) speakingRing.gameObject.SetActive(speaking);
            if (speaking) { var c = LobbyStyle.Ready; c.a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f)); speakingRing.color = c; }
        }
        if (voiceText != null)
        {
            voiceText.text = !connected ? "" : muted ? "마이크 꺼짐" : speaking ? "말하는 중" : "음성 연결됨";
            voiceText.color = muted ? LobbyStyle.Error : speaking ? LobbyStyle.Ready : LobbyStyle.TextDim;
        }
    }

    public void SetOpen()
    {
        gameObject.SetActive(true);
        _onKick = null;
        frame.color = new Color(0.09f, 0.1f, 0.12f, 0.8f);
        avatar.color = new Color(0.15f, 0.17f, 0.2f);
        initialText.text = "+";
        nameText.text = "빈 자리";
        nameText.color = LobbyStyle.TextDim;
        roleText.text = "";
        statusText.text = "대원을 기다리는 중";
        statusText.color = new Color(0.4f, 0.44f, 0.5f);
        statusBar.color = new Color(0.16f, 0.18f, 0.21f);
        kickButton.gameObject.SetActive(false);
        SetVoice(false, false, false);
    }

    public void SetClosed()
    {
        gameObject.SetActive(false);
    }
}
