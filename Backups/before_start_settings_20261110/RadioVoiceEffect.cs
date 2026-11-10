/* [ 코드 설명 ] 무전 채널로 들리는 다른 대원의 목소리에만 무전기 음질(치지직)을 입힙니다. 무전 채널의 참가자마다 Vivox 참가자 오디오 탭을 만들어 그 대원의 무전 음성을 Unity AudioSource로 받고 RadioVoiceFilter를 겁니다. 근거리(3D) 육성은 건드리지 않습니다. InGameVoiceChat이 자동으로 붙이며, 이 컴포넌트를 끄면 탭을 모두 없애 원래 소리로 돌아갑니다. */
using System.Collections.Generic;
using Unity.Services.Vivox;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class RadioVoiceEffect : MonoBehaviour
{
    [Range(0f, 2f)] public float volume = 1f;
    [Min(0.1f)] public float refreshInterval = 0.5f;

    readonly Dictionary<string, VivoxParticipant> taps = new Dictionary<string, VivoxParticipant>();
    readonly HashSet<string> seen = new HashSet<string>();
    readonly List<string> stale = new List<string>();
    readonly HashSet<string> failed = new HashSet<string>();
    string channel;
    float nextRefresh;

    public int ActiveTapCount => taps.Count;

    void OnDisable() => ClearAll();
    void OnDestroy() => ClearAll();

    void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + refreshInterval;
        Refresh();
    }

    void Refresh()
    {
        if (!VoiceChatService.Exists) { ClearAll(); return; }
        var voice = VoiceChatService.Instance;
        if (voice == null || !voice.IsLoggedIn) { ClearAll(); return; }

        string radio = voice.RadioChannel;
        if (radio != channel) { ClearAll(); channel = radio; }

        IReadOnlyCollection<VivoxParticipant> participants = null;
        try
        {
            if (VivoxService.Instance.ActiveChannels.TryGetValue(radio, out var list)) participants = list;
        }
        catch (System.Exception) { }
        if (participants == null) { ClearAll(); return; }

        seen.Clear();
        foreach (var participant in participants)
        {
            if (participant == null || participant.IsSelf || string.IsNullOrEmpty(participant.PlayerId)) continue;
            seen.Add(participant.PlayerId);
            if (!taps.ContainsKey(participant.PlayerId) && !failed.Contains(participant.PlayerId)) CreateTap(participant);
        }

        stale.Clear();
        foreach (var id in taps.Keys) if (!seen.Contains(id)) stale.Add(id);
        foreach (var id in stale) RemoveTap(id);
    }

    void CreateTap(VivoxParticipant participant)
    {
        try
        {
            // silenceInChannelAudioMix = true: 이 대원의 무전 음성은 Vivox 기본 출력 대신 이 AudioSource로만 나온다 (두 번 들리지 않게)
            var go = participant.CreateVivoxParticipantTap("RadioVoice " + participant.DisplayName, true);
            if (go == null) { failed.Add(participant.PlayerId); return; }
            go.transform.SetParent(transform, false);

            var source = participant.ParticipantTapAudioSource != null ? participant.ParticipantTapAudioSource : go.GetComponent<AudioSource>();
            if (source != null)
            {
                source.spatialBlend = 0f;   // 무전은 거리와 상관없이 귀에 대고 듣는 소리
                source.volume = volume;
                source.bypassEffects = false;
            }
            // 탭 컴포넌트 뒤에 붙여야 탭이 넣어 준 음성에 필터가 적용된다
            if (go.GetComponent<RadioVoiceFilter>() == null) go.AddComponent<RadioVoiceFilter>();
            taps[participant.PlayerId] = participant;
        }
        catch (System.Exception e)
        {
            failed.Add(participant.PlayerId);
            Debug.LogWarning("[무전 효과] " + participant.DisplayName + "의 오디오 탭을 만들지 못해 원래 소리로 둡니다: " + e.Message);
        }
    }

    void RemoveTap(string id)
    {
        if (taps.TryGetValue(id, out var participant))
        {
            try { participant.DestroyVivoxParticipantTap(); } catch (System.Exception) { }
        }
        taps.Remove(id);
    }

    void ClearAll()
    {
        if (taps.Count > 0)
        {
            stale.Clear();
            stale.AddRange(taps.Keys);
            foreach (var id in stale) RemoveTap(id);
        }
        failed.Clear();
    }
}
