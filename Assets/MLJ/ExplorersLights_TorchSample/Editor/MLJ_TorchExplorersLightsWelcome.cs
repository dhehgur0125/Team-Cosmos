// v1.0  ?Explorer's Lights (Free Torch) ?Welcome Window

using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;

namespace MLJ.ExplorersLights.Editor
{
    public class ExplorersLights_TorchSampleWelcome : EditorWindow
    {
        // --- Config ---
        const string HasShownKey = "ExplorersLights_FreeTorch_WelcomeShown";
        const string ReadmeFileHint = "ExplorersLights_FreeSample_Readme_v1_0"; // name hint, no extension required
        const string PublisherURL = "https://assetstore.unity.com/publishers/96895";
        const string FullPackURL = "https://assetstore.unity.com/packages/3d/props/tools/sci-fi-lighting-pack-explorers-lights-310497";

        // "ExplorersLights_FullPack_Banner.png" ?the window will find it automatically.
        const string BannerSearchQuery = "ExplorersLights_FullPack_Banner";

        static readonly Vector2 MinWin = new Vector2(580, 560);
        static readonly Vector2 MaxWin = new Vector2(920, 720);

        GUIStyle _title, _body, _author, _footer;
        Texture2D _banner;

        // --- Auto-show once on editor load (not in play mode) ---
        [InitializeOnLoadMethod]
        static void ShowOnceOnLoad()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorPrefs.GetBool(HasShownKey, false)) return;

            EditorApplication.delayCall += () =>
            {
                var w = CreateWindow();
                w.ShowPopup();
                EditorPrefs.SetBool(HasShownKey, true);
            };
        }

        [MenuItem("Tools/Explorer's Lights/Show Welcome Message")]
        public static void ShowManually()
        {
            var w = CreateWindow();
            w.Show();
        }

        static ExplorersLights_TorchSampleWelcome CreateWindow()
        {
            var win = GetWindow<ExplorersLights_TorchSampleWelcome>(true, "Explorer's Lights");
            win.minSize = MinWin;
            win.maxSize = MaxWin;
            CenterOnMain(win, MinWin);
            return win;
        }

        void OnEnable()
        {
            // Styles
            _title = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            _body = new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 12, wordWrap = true };
            _author = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Italic, normal = { textColor = Color.gray } };
            _footer = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter };

            // Try to locate a banner and cache it
            _banner = FindBannerTexture();

            // Ensure window stays within limits if Unity restores a tiny cached size
            if (position.width < MinWin.x || position.height < MinWin.y)
                position = new Rect(position.x, position.y, MinWin.x, MinWin.y);
        }

        void OnGUI()
        {
            var pad = 14f;
            GUILayout.BeginVertical();
            GUILayout.Space(pad);

            // Banner (optional)
            if (_banner)
            {
                float avail = position.width - pad * 2f;
                float scale = Mathf.Min(1f, avail / _banner.width);
                var rect = GUILayoutUtility.GetRect(_banner.width * scale, _banner.height * scale, GUILayout.ExpandWidth(false));
                rect.x = (position.width - rect.width) * 0.5f; // center
                EditorGUI.DrawPreviewTexture(rect, _banner, null, ScaleMode.ScaleToFit);
                GUILayout.Space(12);
            }

            // Title
            GUILayout.Label("Thanks for trying Explorer's Lights!", _title);
            GUILayout.Space(8);

            // Body
            GUILayout.Label(
                "This free torch sample is part of the Explorer's Lights pack ?a set of futuristic, story-driven light props for adventure and exploration games.\n\n" +
                "Feel free to inspect materials, test in different pipelines, and adapt it to your needs.\n\n" +
                "If you find it useful, consider checking out the full pack or leaving a quick review.",
                _body);

            GUILayout.Space(4);
            GUILayout.Label("//Martin Ljungblad", _author);
            GUILayout.Space(10);

            // Buttons (responsive)
            GUILayout.FlexibleSpace();
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Open Readme", GUILayout.Width(130), GUILayout.Height(28)))
                    OpenReadme();

                GUILayout.Space(10);

                if (GUILayout.Button("Visit Full Pack", GUILayout.Width(130), GUILayout.Height(28)))
                    Application.OpenURL(FullPackURL);

                GUILayout.Space(10);

                if (GUILayout.Button("More Assets", GUILayout.Width(130), GUILayout.Height(28)))
                    Application.OpenURL(PublisherURL);

                GUILayout.Space(10);

                if (GUILayout.Button("Close", GUILayout.Width(110), GUILayout.Height(28)))
                    Close();

                GUILayout.FlexibleSpace();
            }

            GUILayout.Space(10);
            GUILayout.Label("Explorer's Lights ?Free Torch Sample", _footer);
            GUILayout.Space(6);
            GUILayout.EndVertical();
        }

        // --- Actions ---
        void OpenReadme()
        {
            var path = FindReadmePDF();
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                // Open with the OS default PDF app (more reliable than OpenURL for files)
                EditorUtility.OpenWithDefaultApp(path);
            }
            else
            {
                Debug.LogWarning("[Explorer's Lights] Could not find the README in the project. " +
                                 "Expected a file named like '" + ReadmeFileHint + ".pdf' somewhere under Assets/.");
            }
        }


        // --- Helpers ---
        static void CenterOnMain(EditorWindow w, Vector2 size)
        {
            var main = EditorGUIUtility.GetMainWindowPosition();
            var pos = new Rect(
                main.x + (main.width - size.x) * 0.5f,
                main.y + (main.height - size.y) * 0.5f,
                Mathf.Max(size.x, 480), Mathf.Max(size.y, 320));
            w.position = pos;
        }

        static Texture2D FindBannerTexture()
        {
            var guids = AssetDatabase.FindAssets(BannerSearchQuery);
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (tex != null) return tex;
            }
            return null; // safe to continue without a banner
        }

        static string FindReadmePDF()
        {
            // 1) Exact name (no extension), any type (DefaultAsset catches PDFs)
            // Quotes make it an exact-name match in FindAssets.
            var exact = AssetDatabase.FindAssets($"\"{ReadmeFileHint}\"");
            foreach (var g in exact)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.EndsWith(".pdf", System.StringComparison.OrdinalIgnoreCase))
                    return Path.GetFullPath(p);
            }

            // 2) Name contains the hint, type DefaultAsset, anywhere under Assets/
            var byType = AssetDatabase.FindAssets($"{ReadmeFileHint} t:DefaultAsset");
            foreach (var g in byType)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.EndsWith(".pdf", System.StringComparison.OrdinalIgnoreCase))
                    return Path.GetFullPath(p);
            }

            // 3) Fallback: any PDF under a Documentation folder
            var all = AssetDatabase.FindAssets("t:DefaultAsset");
            foreach (var g in all)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.EndsWith(".pdf", System.StringComparison.OrdinalIgnoreCase) &&
                    p.ToLowerInvariant().Contains("/documentation/"))
                    return Path.GetFullPath(p);
            }
            return null;
        }

    }
}
