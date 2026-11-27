using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 5층(시작 층) 연출: 몬스터가 우주선에 들어왔다는 것을 알려 준다. FloorTransition.Awake가 5층에서만 붙인다.
///  - 왼쪽 목표: 1층 격납고로 대피 → 계단으로 내려가기 (5층은 임무 없이 바로 내려갈 수 있음)
///  - 게임을 처음 시작했을 때만(다른 층에서 올라온 경우 제외): 경보음 + 함내 방송(글자만) + 멀리서 쿵쿵 소리
///  - 이 PC에서 처음 하는 게임이면 이어서 조작 도움말 (PlayerPrefs VL_Tips_Shown)
/// 소리는 코드로 만든 임시 소리이고, 방송은 화면 글자로만 나온다.
/// </summary>
[DisallowMultipleComponent]
public sealed class Floor5Intro : MonoBehaviour
{
    const string TipsKey = "VL_Tips_Shown";
    const float ToastGap = 6.5f;

    MissionHud _hud;
    AudioSource _audio;
    static AudioClip _alarm, _thud;

    public void Init(TMP_FontAsset font, bool gameStart)
    {
        _hud = new MissionHud(transform, font, "5층 · 승무원 구역");
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false; _audio.spatialBlend = 0f;
        if (gameStart) StartCoroutine(Broadcast());
    }

    void Update()
    {
        if (_hud == null) return;
        _hud.BeginObjectives();
        _hud.Raw("<color=#FCA5A5>외부 생명체 침입</color> — 1층 격납고의 탈출선으로 대피");
        _hud.Line(false, "계단(A 또는 B)으로 아래층 내려가기");
        _hud.Raw("<color=#8A96A3>층마다 시스템을 복구해야 아래층 계단이 열려요</color>");
        _hud.EndObjectives();
        _hud.Tick();
    }

    IEnumerator Broadcast()
    {
        // 로딩 화면·도착 연출이 끝나고 조작할 수 있게 된 뒤에 시작
        while (LoadingScreen.IsShowing || FloorTransition.InCutscene) yield return null;
        yield return new WaitForSeconds(1.5f);

        _audio.PlayOneShot(Alarm(), 0.45f);
        _hud.Banner("외부 생명체 침입", "함내 방송 · 하층 화물 구역에서 침입이 감지되었습니다", MissionHud.Red);
        yield return new WaitForSeconds(4f);
        _audio.PlayOneShot(Thud(), 0.7f);
        yield return new WaitForSeconds(0.9f);
        _audio.PlayOneShot(Thud(), 0.5f);
        yield return new WaitForSeconds(2.1f);

        string tag = "<color=#FCA5A5>[함내 방송]</color> ";
        _hud.Toast(tag + "전 승무원은 하층 격납고로 대피하십시오. 탈출선은 1층 격납 베이에 있습니다.");
        yield return new WaitForSeconds(ToastGap);
        _hud.Toast(tag + "각 층 시스템이 손상되었습니다. 층마다 복구해야 아래층 계단이 열립니다.");
        yield return new WaitForSeconds(ToastGap);
        _hud.Toast(tag + "생명체는 소리에 반응합니다. 총소리와 발소리를 줄이십시오.");
        yield return new WaitForSeconds(ToastGap);

        if (Pref(TipsKey) == 1) yield break;
        string tip = "<color=#7DD3FC>도움말</color>  ";
        string[] tips =
        {
            tip + "권총을 든 채 <b>[" + GameKeys.Label(GameAction.Flashlight) + "]</b> 손전등 켜기·끄기",
            tip + "<b>[" + GameKeys.Label(GameAction.Interact) + "]</b> 줍기·장치 사용 · <b>[" + GameKeys.Label(GameAction.Crouch) + "]</b> 앉으면 발소리가 나지 않아요",
            tip + "달리면 지구력이 줄어요 (HP 아래 막대). 다 쓰면 잠시 달릴 수 없어요",
            tip + "쓰러진 몬스터는 2~3분 뒤 다시 일어나요. 꿈틀거리며 으르렁대면 곧 일어난다는 신호",
            tip + "계단에서 살아 있는 대원이 모두 모여 <b>[" + GameKeys.Label(GameAction.Interact) + "]</b>를 누르면 함께 이동해요",
        };
        foreach (var t in tips)
        {
            _hud.Toast(t);
            yield return new WaitForSeconds(ToastGap);
        }
        try { PlayerPrefs.SetInt(TipsKey, 1); PlayerPrefs.Save(); } catch { }
    }

    static int Pref(string key) { try { return PlayerPrefs.GetInt(key, 0); } catch { return 0; } }

    /// <summary>오르내리는 두 음 경보 (3번, 약 3.6초)</summary>
    static AudioClip Alarm()
    {
        if (_alarm != null) return _alarm;
        const int rate = 22050; const float seconds = 3.6f;
        int n = (int)(rate * seconds);
        var data = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float cycle = t % 1.2f;
            float freq = cycle < 0.6f ? 620f : 480f;
            phase += 2f * Mathf.PI * freq / rate;
            float env = Mathf.Clamp01(cycle / 0.03f) * Mathf.Clamp01((0.6f - (cycle % 0.6f)) / 0.05f) * Mathf.Clamp01((seconds - t) / 0.2f);
            float square = Mathf.Sign(Mathf.Sin(phase)) * 0.35f + Mathf.Sin(phase) * 0.4f;
            data[i] = square * env * 0.6f;
        }
        _alarm = AudioClip.Create("Floor5Alarm", n, 1, rate, false);
        _alarm.SetData(data, 0);
        return _alarm;
    }

    /// <summary>멀리서 나는 둔한 쿵 소리 (0.9초)</summary>
    static AudioClip Thud()
    {
        if (_thud != null) return _thud;
        const int rate = 22050; const float seconds = 0.9f;
        int n = (int)(rate * seconds);
        var data = new float[n];
        var rng = new System.Random(3);
        float lp = 0f, phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float env = Mathf.Exp(-t * 6f) * Mathf.Clamp01(t / 0.01f);
            phase += 2f * Mathf.PI * (48f - 20f * t) / rate;
            lp = Mathf.Lerp(lp, (float)(rng.NextDouble() * 2 - 1), 0.04f);
            data[i] = (Mathf.Sin(phase) * 0.8f + lp * 0.9f) * env;
        }
        _thud = AudioClip.Create("Floor5Thud", n, 1, rate, false);
        _thud.SetData(data, 0);
        return _thud;
    }
}
