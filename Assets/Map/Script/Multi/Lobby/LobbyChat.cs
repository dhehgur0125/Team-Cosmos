using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 대기방 채팅. 씬에 배치된 NetworkObject로, 방장이 호스트를 시작하면 자동으로 스폰된다.
/// 클라이언트 → 서버(방장) → 모든 대원 순서로 전달한다.
/// </summary>
public class LobbyChat : NetworkBehaviour
{
    /// <summary>(보낸 사람, 내용, 시스템 메시지 여부)</summary>
    public event Action<string, string, bool> MessageReceived;

    public void Send(string sender, string text)
    {
        if (!IsSpawned) return;
        SubmitRpc(ToFixed64(sender), ToFixed512(text));
    }

    /// <summary>방장만 호출: 모든 대원에게 시스템 메시지.</summary>
    public void SendSystem(string text)
    {
        if (!IsSpawned || !IsServer) return;
        BroadcastRpc(default, ToFixed512(text), true);
    }

    [Rpc(SendTo.Server)]
    void SubmitRpc(FixedString64Bytes sender, FixedString512Bytes text)
    {
        BroadcastRpc(sender, text, false);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void BroadcastRpc(FixedString64Bytes sender, FixedString512Bytes text, bool system)
    {
        MessageReceived?.Invoke(sender.ToString(), text.ToString(), system);
    }

    static FixedString64Bytes ToFixed64(string s)
    {
        var f = new FixedString64Bytes();
        foreach (char c in s ?? "")
            if (f.Append(c) != FormatError.None) break;
        return f;
    }

    static FixedString512Bytes ToFixed512(string s)
    {
        var f = new FixedString512Bytes();
        foreach (char c in s ?? "")
            if (f.Append(c) != FormatError.None) break;
        return f;
    }
}
