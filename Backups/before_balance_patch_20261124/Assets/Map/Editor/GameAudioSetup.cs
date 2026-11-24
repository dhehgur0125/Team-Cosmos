using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 소리 묶음(오디오 믹서) 만들기 (메뉴 Tools/VOID LAST/Settings/Create Audio Mixer).
/// Assets/Resources/Audio/GameMixer.mixer: Master 아래 Effects(효과음)·Music(배경음)·Voice(무전 음성).
/// 각 묶음의 볼륨을 MasterVolume·EffectsVolume·MusicVolume·VoiceVolume 이름으로 열어 두어 GameAudio가 설정 값을 넣는다.
/// Unity에 믹서를 만드는 공개 함수가 없어 에디터 내부 함수를 사용한다. 이미 있으면 다시 만들지 않는다.
/// </summary>
public static class GameAudioSetup
{
    const string Folder = "Assets/Resources/Audio";
    const string Path = Folder + "/GameMixer.mixer";
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [MenuItem("Tools/VOID LAST/Settings/Create Audio Mixer")]
    public static string Create()
    {
        if (AssetDatabase.LoadAssetAtPath<AudioMixer>(Path) != null) return "already exists: " + Path;
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources", "Audio");

        var asm = typeof(Editor).Assembly;
        var ctlType = asm.GetType("UnityEditor.Audio.AudioMixerController");
        var groupType = asm.GetType("UnityEditor.Audio.AudioMixerGroupController");
        var pathType = asm.GetType("UnityEditor.Audio.AudioGroupParameterPath");
        var exposedType = asm.GetType("UnityEditor.Audio.ExposedAudioParameter");

        var controller = ctlType.GetMethod("CreateMixerControllerAtPath", All).Invoke(null, new object[] { Path });
        var master = ctlType.GetProperty("masterGroup", All).GetValue(controller);

        Expose(controller, ctlType, groupType, pathType, exposedType, master, "MasterVolume");
        foreach (var name in new[] { "Effects", "Music", "Voice" })
        {
            var group = ctlType.GetMethod("CreateNewGroup", All).Invoke(controller, new object[] { name, false });
            ctlType.GetMethod("AddChildToParent", All).Invoke(controller, new[] { group, master });
            try { ctlType.GetMethod("AddGroupToCurrentView", All).Invoke(controller, new[] { group }); }
            catch (Exception) { }   // 믹서 창의 '보기' 목록용 (없어도 동작에는 상관없음)
            Expose(controller, ctlType, groupType, pathType, exposedType, group, name + "Volume");
        }

        EditorUtility.SetDirty((UnityEngine.Object)controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(Path);
        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(Path);
        string result = mixer != null ? "created " + Path + " groups=" + mixer.FindMatchingGroups("").Length : "failed";
        Debug.Log("[소리 묶음] " + result);
        return result;
    }

    static void Expose(object controller, Type ctlType, Type groupType, Type pathType, Type exposedType, object group, string name)
    {
        var guid = groupType.GetMethod("GetGUIDForVolume", All).Invoke(group, null);
        var path = Activator.CreateInstance(pathType, All, null, new[] { group, guid }, null);
        ctlType.GetMethod("AddExposedParameter", All).Invoke(controller, new[] { path });

        // 방금 추가된 파라미터 이름 바꾸기 (기본 이름은 "MyExposedParam")
        var prop = ctlType.GetProperty("exposedParameters", All);
        var array = (Array)prop.GetValue(controller);
        var guidField = exposedType.GetField("guid", All);
        var nameField = exposedType.GetField("name", All);
        for (int i = 0; i < array.Length; i++)
        {
            var p = array.GetValue(i);
            if (!guidField.GetValue(p).Equals(guid)) continue;
            nameField.SetValue(p, name);
            array.SetValue(p, i);
        }
        prop.SetValue(controller, array);
    }
}
