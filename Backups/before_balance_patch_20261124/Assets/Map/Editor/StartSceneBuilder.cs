using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 처음 화면 씬 만들기 (메뉴 Tools/VOID LAST/Settings/Build Start Scene).
/// Assets/Map/Scenes/Start.unity = 카메라 + EventSystem + StartMenu(화면은 실행할 때 코드로 만든다).
/// 빌드 설정의 맨 앞(0번)에 넣어 게임을 켜면 처음 화면부터 나오게 한다. 다시 실행해도 같은 결과.
/// </summary>
public static class StartSceneBuilder
{
    const string ScenePath = "Assets/Map/Scenes/Start.unity";

    [MenuItem("Tools/VOID LAST/Settings/Build Start Scene")]
    public static string Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "cancelled";
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.025f, 0.035f);
        camGo.AddComponent<AudioListener>();

        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        var menu = new GameObject("StartMenu");
        menu.AddComponent<StartMenu>();

        EditorSceneManager.SaveScene(scene, ScenePath);

        // 빌드 설정: Start를 맨 앞으로
        var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
        AssetDatabase.SaveAssets();   // 빌드 설정 파일을 바로 디스크에 쓴다

        string result = "built " + ScenePath + " · build order: " + string.Join(", ", EditorBuildSettings.scenes.Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path)));
        Debug.Log("[처음 화면] " + result);
        return result;
    }
}
