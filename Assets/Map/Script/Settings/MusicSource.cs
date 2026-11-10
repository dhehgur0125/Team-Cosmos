using UnityEngine;

/// <summary>이 AudioSource를 배경음 크기로 조절한다 (배경 음악·분위기 소리에 붙인다)</summary>
[RequireComponent(typeof(AudioSource))]
public sealed class MusicSource : MonoBehaviour
{
    void Awake()
    {
        var s = GetComponent<AudioSource>();
        s.outputAudioMixerGroup = null;
        GameAudio.Route(s, AudioCategory.Music);
    }
}
