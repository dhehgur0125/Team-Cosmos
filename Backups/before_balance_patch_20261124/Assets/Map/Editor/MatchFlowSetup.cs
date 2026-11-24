// Stage2: 관전 모드 + 게임 오버 화면 오브젝트 배치
// 메뉴: Tools/VOID LAST/Multiplayer/8 Build Spectate & Game Over
#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VoidLast.MapTools
{
    public static class MatchFlowSetup
    {
        const string ScenePath = "Assets/Map/Scenes/Stage2_Ship.unity";

        [MenuItem("Tools/VOID LAST/Multiplayer/8 Build Spectate & Game Over")]
        public static string Build()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var multi = GameObject.Find(MultiplayerSetup.MultiRootName);
            if (multi == null) throw new InvalidOperationException("먼저 Multiplayer 설정(5 Patch Stage2 Scene)을 실행하세요");
            foreach (var n in new[] { "Spectator", "Game Over" })
            {
                var old = multi.transform.Find(n);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MultiplayerSetup.FontAssetPath);

            var spec = new GameObject("Spectator");
            spec.transform.SetParent(multi.transform, false);
            spec.AddComponent<SpectatorSystem>().font = font;

            var over = new GameObject("Game Over");
            over.transform.SetParent(multi.transform, false);
            var g = over.AddComponent<GameOverScreen>();
            g.font = font;
            g.lobbySceneName = "Lobby";

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            const string s = "Match flow: Spectator + Game Over";
            Debug.Log("[MatchFlowSetup] " + s);
            return s;
        }
    }
}
#endif
