// 멀티 대기실(Lobby) 씬 생성기
// 메뉴: Tools/VOID LAST/Build Lobby Scene
#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace VoidLast.MapTools
{
    public static class LobbySceneBuilder
    {
        public const string ScenePath = "Assets/Map/Scenes/Lobby.unity";
        const string FontPath = "Assets/Map/Fonts/NotoSansKR-Regular.otf";
        const string FontAssetPath = "Assets/Map/Fonts/NotoSansKR SDF.asset";
        const string PlayerPrefabPath = "Assets/Player-Legacy/Prefabs/Character/Player.prefab";
        const string PrefabListPath = "Assets/DefaultNetworkPrefabs.asset";

        static TMP_FontAsset _font;
        static Sprite _rounded, _knob, _check;

        [MenuItem("Tools/VOID LAST/Build Lobby Scene")]
        public static string Build()
        {
            _font = EnsureFont();
            _rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            _knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            _check = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------------------------------------------------------- camera / event system
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.025f, 0.035f);
            camGo.AddComponent<AudioListener>();

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();

            // ---------------------------------------------------------- network
            var nmGo = new GameObject("NetworkManager");
            var nm = nmGo.AddComponent<NetworkManager>();
            nmGo.AddComponent<UnityTransport>();
            nm.NetworkConfig = new NetworkConfig
            {
                PlayerPrefab = null,               // 로비에서는 캐릭터를 만들지 않음 (LobbyPlayerSpawner가 게임 씬에서 스폰)
                EnableSceneManagement = true,
                TickRate = 30,
                ForceSamePrefabs = true,
                NetworkTransport = nmGo.GetComponent<UnityTransport>(),
            };
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list != null) nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
            var spawner = nmGo.AddComponent<LobbyPlayerSpawner>();
            spawner.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            spawner.lobbySceneName = "Lobby";

            var chatGo = new GameObject("LobbyChat");
            chatGo.AddComponent<NetworkObject>();
            var chat = chatGo.AddComponent<LobbyChat>();

            // ---------------------------------------------------------- canvas
            var canvasGo = new GameObject("LobbyCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var root = canvasGo.transform;

            var mgrGo = new GameObject("LobbyManager");
            var lm = mgrGo.AddComponent<LobbyManager>();
            lm.chat = chat;
            lm.gameSceneName = "MapScene";

            // background
            var bg = Img(root, "Background", new Color(0.024f, 0.03f, 0.04f), null);
            Stretch(bg.rectTransform);
            var stripe = Img(root, "AccentStripe", LobbyStyle.Accent, null);
            Place(stripe.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(80 + 4, -66), new Vector2(8, 72));

            // header
            var title = Txt(root, "Title", "VOID LAST", 64, Color.white, TextAlignmentOptions.Left, FontStyles.Bold);
            title.characterSpacing = 12;
            Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(110 + 400, -50), new Vector2(800, 80));
            var sub = Txt(root, "Subtitle", "멀티플레이 대기실", 24, LobbyStyle.Accent, TextAlignmentOptions.Left, FontStyles.Normal);
            Place(sub.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(110 + 400, -102), new Vector2(800, 34));
            var line = Img(root, "HeaderLine", new Color(1, 1, 1, 0.08f), null);
            Place(line.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -140), new Vector2(1760, 2));

            // status
            lm.statusText = Txt(root, "Status", "", 22, LobbyStyle.TextDim, TextAlignmentOptions.Left, FontStyles.Normal);
            Place(lm.statusText.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(80 + 700, 50), new Vector2(1400, 40));

            BuildMain(root, lm);
            BuildRoom(root, lm);
            BuildCreate(root, lm);
            BuildLoading(root, lm);

            Selection.activeGameObject = mgrGo;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            // 씬 오브젝트용 NetworkObject 해시는 씬이 저장된 뒤에야 만들어진다 → 저장 후 다시 생성하고 한 번 더 저장
            var validate = typeof(NetworkObject).GetMethod("OnValidate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            foreach (var no in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include))
            {
                if (validate != null) validate.Invoke(no, null);
                EditorUtility.SetDirty(no);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();
            return "lobby scene saved: " + ScenePath;
        }

        // ================================================================== panels
        static void BuildMain(Transform root, LobbyManager lm)
        {
            var panel = Rt("MainPanel", root);
            StretchOffsets(panel, 80, 80, 170, 110);
            lm.mainPanel = panel.gameObject;

            // ---- left card
            var left = Card(panel, "PlayerCard");
            left.anchorMin = new Vector2(0, 0); left.anchorMax = new Vector2(0, 1);
            left.pivot = new Vector2(0, 1);
            left.anchoredPosition = Vector2.zero; left.sizeDelta = new Vector2(560, 0);

            Header(left, "대원 정보", 0);
            Label(left, "닉네임", 90);
            lm.nicknameInput = Input(left, "NicknameInput", "닉네임을 입력하세요", 16);
            TopRow(lm.nicknameInput.GetComponent<RectTransform>(), 126, 64, 40, 40);

            lm.createOpenButton = Btn(left, "CreateRoomButton", "방 만들기", LobbyStyle.Accent, 30, FontStyles.Bold);
            TopRow((RectTransform)lm.createOpenButton.transform, 230, 88, 40, 40);
            lm.quickJoinButton = Btn(left, "QuickJoinButton", "빠른 참가", LobbyStyle.ButtonDark, 26, FontStyles.Bold);
            TopRow((RectTransform)lm.quickJoinButton.transform, 334, 72, 40, 40);

            var div = Img(left, "Divider", new Color(1, 1, 1, 0.08f), null);
            TopRow(div.rectTransform, 446, 2, 40, 40);

            Label(left, "참가 코드로 들어가기", 476);
            lm.joinCodeInput = Input(left, "JoinCodeInput", "참가 코드", 12);
            lm.joinCodeInput.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;
            TopRow(lm.joinCodeInput.GetComponent<RectTransform>(), 512, 64, 40, 200);
            lm.joinCodeButton = Btn(left, "JoinCodeButton", "참가", LobbyStyle.ButtonDark, 24, FontStyles.Bold);
            var jcb = (RectTransform)lm.joinCodeButton.transform;
            jcb.anchorMin = new Vector2(1, 1); jcb.anchorMax = new Vector2(1, 1); jcb.pivot = new Vector2(1, 1);
            jcb.anchoredPosition = new Vector2(-40, -512); jcb.sizeDelta = new Vector2(144, 64);

            var hint = Txt(left, "Hint", "친구가 알려준 참가 코드를 입력하면 비공개 방에도 들어갈 수 있어요.\n방장이 게임을 시작하면 모든 대원이 함께 이동해요.", 19, LobbyStyle.TextDim, TextAlignmentOptions.TopLeft, FontStyles.Normal);
            var hr = hint.rectTransform; hr.anchorMin = new Vector2(0, 0); hr.anchorMax = new Vector2(1, 0); hr.pivot = new Vector2(0.5f, 0);
            hr.anchoredPosition = new Vector2(0, 36); hr.sizeDelta = new Vector2(-80, 90);

            // ---- right card: room list
            var right = Card(panel, "RoomListCard");
            right.anchorMin = new Vector2(0, 0); right.anchorMax = new Vector2(1, 1);
            right.offsetMin = new Vector2(600, 0); right.offsetMax = Vector2.zero;

            Header(right, "열린 방", 0);
            lm.roomCountText = Txt(right, "RoomCount", "", 22, LobbyStyle.TextDim, TextAlignmentOptions.Left, FontStyles.Normal);
            var rc = lm.roomCountText.rectTransform; rc.anchorMin = rc.anchorMax = new Vector2(0, 1); rc.pivot = new Vector2(0, 1);
            rc.anchoredPosition = new Vector2(150, -34); rc.sizeDelta = new Vector2(200, 36);
            lm.refreshButton = Btn(right, "RefreshButton", "새로고침", LobbyStyle.ButtonDark, 22, FontStyles.Normal);
            var rb = (RectTransform)lm.refreshButton.transform; rb.anchorMin = rb.anchorMax = new Vector2(1, 1); rb.pivot = new Vector2(1, 1);
            rb.anchoredPosition = new Vector2(-32, -26); rb.sizeDelta = new Vector2(150, 52);

            // column header
            var cols = Rt("Columns", right);
            cols.anchorMin = new Vector2(0, 1); cols.anchorMax = new Vector2(1, 1); cols.pivot = new Vector2(0.5f, 1);
            cols.anchoredPosition = new Vector2(0, -100); cols.sizeDelta = new Vector2(-64, 32);
            ColumnLabel(cols, "방 이름", 0.0f, 0.45f, TextAlignmentOptions.Left);
            ColumnLabel(cols, "방장", 0.45f, 0.68f, TextAlignmentOptions.Left);
            ColumnLabel(cols, "인원", 0.68f, 0.8f, TextAlignmentOptions.Center);

            var scroll = Scroll(right, "RoomList", out var content);
            var sr = (RectTransform)scroll.transform; sr.anchorMin = new Vector2(0, 0); sr.anchorMax = new Vector2(1, 1);
            sr.offsetMin = new Vector2(32, 32); sr.offsetMax = new Vector2(-32, -140);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 10; vlg.childControlHeight = false; vlg.childControlWidth = true; vlg.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            lm.roomListContent = content;

            lm.emptyListLabel = Txt(right, "EmptyLabel", "열린 방이 없어요.\n<size=22><color=#8A96A3>방을 만들거나 참가 코드로 들어가 보세요.</color></size>", 30, LobbyStyle.Text, TextAlignmentOptions.Center, FontStyles.Normal).gameObject;
            var el = (RectTransform)lm.emptyListLabel.transform; el.anchorMin = el.anchorMax = new Vector2(0.5f, 0.5f); el.sizeDelta = new Vector2(800, 120);

            // row template
            var row = Img(panel, "RoomItemTemplate", LobbyStyle.PanelLight, _rounded);
            row.type = Image.Type.Sliced;
            var rowRt = row.rectTransform; rowRt.sizeDelta = new Vector2(0, 76);
            var item = row.gameObject.AddComponent<LobbyRoomListItem>();
            item.nameText = Txt(row.transform, "Name", "방 이름", 26, LobbyStyle.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            Cell(item.nameText.rectTransform, 0f, 0.45f, 24);
            item.nameText.textWrappingMode = TextWrappingModes.NoWrap; item.nameText.overflowMode = TextOverflowModes.Ellipsis;
            item.hostText = Txt(row.transform, "Host", "방장", 21, LobbyStyle.TextDim, TextAlignmentOptions.Left, FontStyles.Normal);
            Cell(item.hostText.rectTransform, 0.45f, 0.68f, 8);
            item.hostText.textWrappingMode = TextWrappingModes.NoWrap; item.hostText.overflowMode = TextOverflowModes.Ellipsis;
            item.countText = Txt(row.transform, "Count", "1 / 4", 24, LobbyStyle.Text, TextAlignmentOptions.Center, FontStyles.Normal);
            Cell(item.countText.rectTransform, 0.68f, 0.8f, 0);
            item.joinButton = Btn(row.transform, "JoinButton", "참가", LobbyStyle.Accent, 22, FontStyles.Bold);
            var jb = (RectTransform)item.joinButton.transform; jb.anchorMin = jb.anchorMax = new Vector2(1, 0.5f); jb.pivot = new Vector2(1, 0.5f);
            jb.anchoredPosition = new Vector2(-14, 0); jb.sizeDelta = new Vector2(140, 50);
            row.gameObject.SetActive(false);
            lm.roomItemPrefab = item;
        }

        static void BuildRoom(Transform root, LobbyManager lm)
        {
            var panel = Rt("RoomPanel", root);
            StretchOffsets(panel, 80, 80, 170, 110);
            lm.roomPanel = panel.gameObject;

            // ---- top bar
            var top = Card(panel, "RoomHeader");
            top.anchorMin = new Vector2(0, 1); top.anchorMax = new Vector2(1, 1); top.pivot = new Vector2(0.5f, 1);
            top.anchoredPosition = Vector2.zero; top.sizeDelta = new Vector2(0, 120);
            lm.roomNameText = Txt(top, "RoomName", "방 이름", 38, Color.white, TextAlignmentOptions.Left, FontStyles.Bold);
            var rn = lm.roomNameText.rectTransform; rn.anchorMin = rn.anchorMax = new Vector2(0, 0.5f); rn.pivot = new Vector2(0, 0.5f);
            rn.anchoredPosition = new Vector2(36, 14); rn.sizeDelta = new Vector2(900, 64);
            lm.roomNameText.textWrappingMode = TextWrappingModes.NoWrap; lm.roomNameText.overflowMode = TextOverflowModes.Ellipsis;
            lm.roomInfoText = Txt(top, "RoomInfo", "1 / 4명", 22, LobbyStyle.TextDim, TextAlignmentOptions.Left, FontStyles.Normal);
            var ri = lm.roomInfoText.rectTransform; ri.anchorMin = ri.anchorMax = new Vector2(0, 0.5f); ri.pivot = new Vector2(0, 0.5f);
            ri.anchoredPosition = new Vector2(36, -28); ri.sizeDelta = new Vector2(900, 32);

            var codeLabel = Txt(top, "CodeLabel", "참가 코드", 20, LobbyStyle.TextDim, TextAlignmentOptions.Right, FontStyles.Normal);
            var cl = codeLabel.rectTransform; cl.anchorMin = cl.anchorMax = new Vector2(1, 0.5f); cl.pivot = new Vector2(1, 0.5f);
            cl.anchoredPosition = new Vector2(-470, 0); cl.sizeDelta = new Vector2(160, 30);
            lm.roomCodeText = Txt(top, "RoomCode", "------", 46, LobbyStyle.Accent, TextAlignmentOptions.Center, FontStyles.Bold);
            lm.roomCodeText.characterSpacing = 10;
            var rcode = lm.roomCodeText.rectTransform; rcode.anchorMin = rcode.anchorMax = new Vector2(1, 0.5f); rcode.pivot = new Vector2(1, 0.5f);
            rcode.anchoredPosition = new Vector2(-180, 0); rcode.sizeDelta = new Vector2(280, 64);
            lm.copyCodeButton = Btn(top, "CopyCodeButton", "복사", LobbyStyle.ButtonDark, 22, FontStyles.Normal);
            var cb = (RectTransform)lm.copyCodeButton.transform; cb.anchorMin = cb.anchorMax = new Vector2(1, 0.5f); cb.pivot = new Vector2(1, 0.5f);
            cb.anchoredPosition = new Vector2(-32, 0); cb.sizeDelta = new Vector2(130, 56);

            // ---- player slots
            var slotsRt = Rt("Slots", panel);
            slotsRt.anchorMin = new Vector2(0, 1); slotsRt.anchorMax = new Vector2(1, 1); slotsRt.pivot = new Vector2(0.5f, 1);
            slotsRt.anchoredPosition = new Vector2(0, -144); slotsRt.sizeDelta = new Vector2(0, 330);
            var hlg = slotsRt.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 24; hlg.childControlWidth = true; hlg.childControlHeight = true; hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;
            var slots = new List<LobbyPlayerSlot>();
            for (int i = 0; i < 4; i++) slots.Add(Slot(slotsRt, i));
            lm.slots = slots.ToArray();

            // ---- chat
            var chatCard = Card(panel, "ChatCard");
            chatCard.anchorMin = new Vector2(0, 0); chatCard.anchorMax = new Vector2(0, 0); chatCard.pivot = new Vector2(0, 0);
            chatCard.anchoredPosition = Vector2.zero; chatCard.sizeDelta = new Vector2(1180, 300);
            var chatTitle = Txt(chatCard, "ChatTitle", "채팅", 22, LobbyStyle.TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            var ct = chatTitle.rectTransform; ct.anchorMin = ct.anchorMax = new Vector2(0, 1); ct.pivot = new Vector2(0, 1);
            ct.anchoredPosition = new Vector2(24, -14); ct.sizeDelta = new Vector2(300, 30);

            var chatScroll = Scroll(chatCard, "ChatLog", out var chatContent);
            var cs = (RectTransform)chatScroll.transform; cs.anchorMin = new Vector2(0, 0); cs.anchorMax = new Vector2(1, 1);
            cs.offsetMin = new Vector2(24, 84); cs.offsetMax = new Vector2(-24, -50);
            var log = Txt(chatContent, "LogText", "", 21, LobbyStyle.Text, TextAlignmentOptions.BottomLeft, FontStyles.Normal);
            log.richText = true;
            var lr = log.rectTransform; lr.anchorMin = new Vector2(0, 1); lr.anchorMax = new Vector2(1, 1); lr.pivot = new Vector2(0.5f, 1);
            lr.anchoredPosition = Vector2.zero; lr.sizeDelta = new Vector2(0, 0);
            log.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var cvlg = chatContent.gameObject.AddComponent<VerticalLayoutGroup>();
            cvlg.childControlHeight = true; cvlg.childControlWidth = true; cvlg.childForceExpandHeight = false; cvlg.childAlignment = TextAnchor.LowerLeft;
            chatContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            lm.chatLogText = log;
            lm.chatScroll = chatScroll;

            lm.chatInput = Input(chatCard, "ChatInput", "메시지를 입력하고 Enter", 60);
            var ci = lm.chatInput.GetComponent<RectTransform>(); ci.anchorMin = new Vector2(0, 0); ci.anchorMax = new Vector2(1, 0); ci.pivot = new Vector2(0, 0);
            ci.offsetMin = new Vector2(24, 20); ci.offsetMax = new Vector2(-176, 74);
            lm.chatSendButton = Btn(chatCard, "ChatSendButton", "보내기", LobbyStyle.ButtonDark, 22, FontStyles.Normal);
            var csb = (RectTransform)lm.chatSendButton.transform; csb.anchorMin = csb.anchorMax = new Vector2(1, 0); csb.pivot = new Vector2(1, 0);
            csb.anchoredPosition = new Vector2(-24, 20); csb.sizeDelta = new Vector2(140, 54);

            // ---- actions
            var act = Card(panel, "ActionCard");
            act.anchorMin = new Vector2(1, 0); act.anchorMax = new Vector2(1, 0); act.pivot = new Vector2(1, 0);
            act.anchoredPosition = Vector2.zero; act.sizeDelta = new Vector2(552, 300);
            lm.startHintText = Txt(act, "StartHint", "", 21, LobbyStyle.TextDim, TextAlignmentOptions.Center, FontStyles.Normal);
            var sh = lm.startHintText.rectTransform; sh.anchorMin = new Vector2(0, 1); sh.anchorMax = new Vector2(1, 1); sh.pivot = new Vector2(0.5f, 1);
            sh.anchoredPosition = new Vector2(0, -22); sh.sizeDelta = new Vector2(-48, 56);

            lm.readyButton = Btn(act, "ReadyButton", "준비", LobbyStyle.Accent, 32, FontStyles.Bold);
            lm.readyButtonText = lm.readyButton.GetComponentInChildren<TMP_Text>();
            Bottom((RectTransform)lm.readyButton.transform, 112, 96);
            lm.startButton = Btn(act, "StartButton", "게임 시작", LobbyStyle.Accent, 32, FontStyles.Bold);
            Bottom((RectTransform)lm.startButton.transform, 112, 96);
            lm.leaveButton = Btn(act, "LeaveButton", "나가기", LobbyStyle.ButtonDark, 24, FontStyles.Normal);
            Bottom((RectTransform)lm.leaveButton.transform, 24, 68);

            panel.gameObject.SetActive(false);
        }

        static LobbyPlayerSlot Slot(Transform parent, int i)
        {
            var frame = Img(parent, "Slot" + (i + 1), LobbyStyle.PanelLight, _rounded);
            frame.type = Image.Type.Sliced;
            var slot = frame.gameObject.AddComponent<LobbyPlayerSlot>();
            slot.frame = frame;

            slot.avatar = Img(frame.transform, "Avatar", LobbyStyle.Accent, _knob);
            var ar = slot.avatar.rectTransform; ar.anchorMin = ar.anchorMax = new Vector2(0.5f, 1); ar.pivot = new Vector2(0.5f, 1);
            ar.anchoredPosition = new Vector2(0, -30); ar.sizeDelta = new Vector2(128, 128);
            slot.initialText = Txt(slot.avatar.transform, "Initial", "?", 64, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(slot.initialText.rectTransform);

            slot.nameText = Txt(frame.transform, "Name", "대원", 28, LobbyStyle.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            var nr = slot.nameText.rectTransform; nr.anchorMin = new Vector2(0, 1); nr.anchorMax = new Vector2(1, 1); nr.pivot = new Vector2(0.5f, 1);
            nr.anchoredPosition = new Vector2(0, -170); nr.sizeDelta = new Vector2(-32, 50);
            slot.nameText.textWrappingMode = TextWrappingModes.NoWrap; slot.nameText.overflowMode = TextOverflowModes.Ellipsis;
            slot.roleText = Txt(frame.transform, "Role", "대원", 20, LobbyStyle.TextDim, TextAlignmentOptions.Center, FontStyles.Normal);
            var rr = slot.roleText.rectTransform; rr.anchorMin = new Vector2(0, 1); rr.anchorMax = new Vector2(1, 1); rr.pivot = new Vector2(0.5f, 1);
            rr.anchoredPosition = new Vector2(0, -220); rr.sizeDelta = new Vector2(-32, 32);

            slot.statusBar = Img(frame.transform, "StatusBar", LobbyStyle.Ready, null);
            var sb = slot.statusBar.rectTransform; sb.anchorMin = new Vector2(0, 0); sb.anchorMax = new Vector2(1, 0); sb.pivot = new Vector2(0.5f, 0);
            sb.anchoredPosition = new Vector2(0, 0); sb.sizeDelta = new Vector2(0, 8);
            slot.statusText = Txt(frame.transform, "Status", "준비 완료", 24, LobbyStyle.Ready, TextAlignmentOptions.Center, FontStyles.Bold);
            var st = slot.statusText.rectTransform; st.anchorMin = new Vector2(0, 0); st.anchorMax = new Vector2(1, 0); st.pivot = new Vector2(0.5f, 0);
            st.anchoredPosition = new Vector2(0, 22); st.sizeDelta = new Vector2(-32, 40);

            slot.kickButton = Btn(frame.transform, "KickButton", "내보내기", new Color(0.25f, 0.1f, 0.1f), 18, FontStyles.Normal);
            var kb = (RectTransform)slot.kickButton.transform; kb.anchorMin = kb.anchorMax = new Vector2(1, 1); kb.pivot = new Vector2(1, 1);
            kb.anchoredPosition = new Vector2(-12, -12); kb.sizeDelta = new Vector2(104, 38);
            slot.kickButton.gameObject.SetActive(false);
            return slot;
        }

        static void BuildCreate(Transform root, LobbyManager lm)
        {
            var dim = Img(root, "CreateRoomDialog", new Color(0, 0, 0, 0.62f), null);
            Stretch(dim.rectTransform);
            lm.createPanel = dim.gameObject;

            var card = Img(dim.transform, "Card", new Color(0.07f, 0.085f, 0.105f, 1f), _rounded);
            card.type = Image.Type.Sliced;
            var cr = card.rectTransform; cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f); cr.sizeDelta = new Vector2(680, 560);
            var accent = Img(card.transform, "Accent", LobbyStyle.Accent, null);
            var acr = accent.rectTransform; acr.anchorMin = new Vector2(0, 1); acr.anchorMax = new Vector2(1, 1); acr.pivot = new Vector2(0.5f, 1); acr.sizeDelta = new Vector2(0, 6);

            Header(cr, "방 만들기", 0);
            Label(cr, "방 이름", 96);
            lm.roomNameInput = Input(cr, "RoomNameInput", "방 이름", 30);
            TopRow(lm.roomNameInput.GetComponent<RectTransform>(), 132, 64, 40, 40);

            Label(cr, "최대 인원", 228);
            var btns = new List<Button>();
            for (int i = 0; i < 3; i++)
            {
                var b = Btn(cr, "Max" + (i + 2), (i + 2) + "명", LobbyStyle.ButtonDark, 24, FontStyles.Bold);
                var r = (RectTransform)b.transform; r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
                r.anchoredPosition = new Vector2(40 + i * 204, -264); r.sizeDelta = new Vector2(192, 60);
                btns.Add(b);
            }
            lm.maxPlayerButtons = btns.ToArray();

            lm.privateToggle = Tog(cr, "PrivateToggle", "비공개 방  <size=18><color=#8A96A3>(목록에 안 보이고 참가 코드로만 들어와요)</color></size>");
            TopRow((RectTransform)lm.privateToggle.transform, 356, 48, 40, 40);

            lm.createCancelButton = Btn(cr, "CancelButton", "취소", LobbyStyle.ButtonDark, 24, FontStyles.Normal);
            var cb = (RectTransform)lm.createCancelButton.transform; cb.anchorMin = cb.anchorMax = new Vector2(0, 0); cb.pivot = new Vector2(0, 0);
            cb.anchoredPosition = new Vector2(40, 40); cb.sizeDelta = new Vector2(220, 72);
            lm.createConfirmButton = Btn(cr, "ConfirmButton", "만들기", LobbyStyle.Accent, 28, FontStyles.Bold);
            var ok = (RectTransform)lm.createConfirmButton.transform; ok.anchorMin = ok.anchorMax = new Vector2(1, 0); ok.pivot = new Vector2(1, 0);
            ok.anchoredPosition = new Vector2(-40, 40); ok.sizeDelta = new Vector2(340, 72);

            dim.gameObject.SetActive(false);
        }

        static void BuildLoading(Transform root, LobbyManager lm)
        {
            var dim = Img(root, "LoadingOverlay", new Color(0, 0, 0, 0.66f), null);
            Stretch(dim.rectTransform);
            var card = Img(dim.transform, "Card", LobbyStyle.Panel, _rounded);
            card.type = Image.Type.Sliced;
            var cr = card.rectTransform; cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f); cr.sizeDelta = new Vector2(620, 150);
            var bar = Img(card.transform, "Bar", LobbyStyle.Accent, null);
            var br = bar.rectTransform; br.anchorMin = new Vector2(0, 0); br.anchorMax = new Vector2(0, 1); br.pivot = new Vector2(0, 0.5f); br.sizeDelta = new Vector2(6, 0);
            lm.loadingText = Txt(card.transform, "Text", "연결하는 중...", 28, Color.white, TextAlignmentOptions.Center, FontStyles.Normal);
            Stretch(lm.loadingText.rectTransform);
            lm.loadingOverlay = dim.gameObject;
            dim.gameObject.SetActive(false);
        }

        // ================================================================== widgets
        static RectTransform Rt(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        }

        static void StretchOffsets(RectTransform r, float left, float right, float top, float bottom)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top);
        }

        static void Place(RectTransform r, Vector2 anchor, Vector2 pivotUnused, Vector2 pos, Vector2 size)
        {
            r.anchorMin = r.anchorMax = anchor; r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = pos; r.sizeDelta = size;
        }

        /// <summary>부모 위에서부터 y만큼 내려온 가로 줄 배치.</summary>
        static void TopRow(RectTransform r, float y, float height, float left, float right)
        {
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
            r.offsetMin = new Vector2(left, -y - height); r.offsetMax = new Vector2(-right, -y);
        }

        static void Bottom(RectTransform r, float y, float height)
        {
            r.anchorMin = new Vector2(0, 0); r.anchorMax = new Vector2(1, 0); r.pivot = new Vector2(0.5f, 0);
            r.offsetMin = new Vector2(24, y); r.offsetMax = new Vector2(-24, y + height);
        }

        static void Cell(RectTransform r, float x0, float x1, float padLeft)
        {
            r.anchorMin = new Vector2(x0, 0); r.anchorMax = new Vector2(x1, 1);
            r.offsetMin = new Vector2(padLeft, 0); r.offsetMax = new Vector2(-8, 0);
        }

        static RectTransform Card(Transform parent, string name)
        {
            var img = Img(parent, name, LobbyStyle.Panel, _rounded);
            img.type = Image.Type.Sliced;
            return img.rectTransform;
        }

        static void Header(RectTransform card, string text, float y)
        {
            var t = Txt(card, "Header", text, 30, Color.white, TextAlignmentOptions.Left, FontStyles.Bold);
            var r = t.rectTransform; r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
            r.offsetMin = new Vector2(40, -y - 80); r.offsetMax = new Vector2(-40, -y - 24);
        }

        static void Label(RectTransform card, string text, float y)
        {
            var t = Txt(card, "Label_" + text, text, 21, LobbyStyle.TextDim, TextAlignmentOptions.Left, FontStyles.Normal);
            TopRow(t.rectTransform, y, 30, 40, 40);
        }

        static void ColumnLabel(RectTransform parent, string text, float x0, float x1, TextAlignmentOptions align)
        {
            var t = Txt(parent, "Col_" + text, text, 19, LobbyStyle.TextDim, align, FontStyles.Normal);
            Cell(t.rectTransform, x0, x1, align == TextAlignmentOptions.Left ? 24 : 0);
        }

        static Image Img(Transform parent, string name, Color c, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = c;
            if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
            img.raycastTarget = name.Contains("Dialog") || name.Contains("Overlay") || name.Contains("Background");
            return img;
        }

        static TextMeshProUGUI Txt(Transform parent, string name, string text, float size, Color c, TextAlignmentOptions align, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = _font;
            t.text = text;
            t.fontSize = size;
            t.color = c;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            return t;
        }

        static Button Btn(Transform parent, string name, string label, Color bg, float fontSize, FontStyles style)
        {
            var img = Img(parent, name, bg, _rounded);
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            colors.colorMultiplier = 1.2f;
            b.colors = colors;
            var t = Txt(img.transform, "Label", label, fontSize, Color.white, TextAlignmentOptions.Center, style);
            Stretch(t.rectTransform);
            return b;
        }

        static TMP_InputField Input(Transform parent, string name, string placeholder, int limit)
        {
            var img = Img(parent, name, new Color(0.04f, 0.05f, 0.065f), _rounded);
            img.raycastTarget = true;
            var area = Rt("Text Area", img.transform);
            Stretch(area); area.offsetMin = new Vector2(18, 6); area.offsetMax = new Vector2(-18, -6);
            area.gameObject.AddComponent<RectMask2D>();
            var ph = Txt(area, "Placeholder", placeholder, 24, new Color(0.45f, 0.5f, 0.56f), TextAlignmentOptions.Left, FontStyles.Italic);
            Stretch(ph.rectTransform);
            var tx = Txt(area, "Text", "", 24, LobbyStyle.Text, TextAlignmentOptions.Left, FontStyles.Normal);
            Stretch(tx.rectTransform);
            tx.richText = false;
            var input = img.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = tx;
            input.placeholder = ph;
            input.fontAsset = _font;
            input.pointSize = 24;
            input.characterLimit = limit;
            input.caretColor = LobbyStyle.Accent;
            input.customCaretColor = true;
            input.selectionColor = new Color(0.88f, 0.27f, 0.23f, 0.35f);
            input.richText = false;
            return input;
        }

        static Toggle Tog(Transform parent, string name, string label)
        {
            var root = Rt(name, parent);
            var tog = root.gameObject.AddComponent<Toggle>();
            var box = Img(root, "Box", new Color(0.04f, 0.05f, 0.065f), _rounded);
            box.raycastTarget = true;
            var br = box.rectTransform; br.anchorMin = br.anchorMax = new Vector2(0, 0.5f); br.pivot = new Vector2(0, 0.5f);
            br.anchoredPosition = Vector2.zero; br.sizeDelta = new Vector2(40, 40);
            var check = Img(box.transform, "Check", LobbyStyle.Accent, _check);
            check.type = Image.Type.Simple;
            var cr = check.rectTransform; Stretch(cr); cr.offsetMin = new Vector2(6, 6); cr.offsetMax = new Vector2(-6, -6);
            var t = Txt(root, "Label", label, 22, LobbyStyle.Text, TextAlignmentOptions.Left, FontStyles.Normal);
            t.richText = true;
            var tr = t.rectTransform; Stretch(tr); tr.offsetMin = new Vector2(56, 0);
            tog.targetGraphic = box;
            tog.graphic = check;
            tog.isOn = false;
            return tog;
        }

        static ScrollRect Scroll(Transform parent, string name, out RectTransform content)
        {
            var root = Rt(name, parent);
            var sr = root.gameObject.AddComponent<ScrollRect>();
            var viewport = Rt("Viewport", root);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            var vimg = viewport.gameObject.AddComponent<Image>();
            vimg.color = new Color(0, 0, 0, 0.001f);   // 휠 스크롤 입력용
            content = Rt("Content", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0, 0);
            sr.viewport = viewport;
            sr.content = content;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 30;
            return sr;
        }

        // ================================================================== font / build settings
        static TMP_FontAsset EnsureFont()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null) return existing;

            AssetDatabase.ImportAsset(FontPath);
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) { Debug.LogError("[LobbyBuilder] 폰트를 찾을 수 없어요: " + FontPath); return TMP_Settings.defaultFontAsset; }

            // 동적 아틀라스: 실제로 쓰인 글자만 1024 텍스처에 올라가서 메모리가 적다
            var fa = TMP_FontAsset.CreateFontAsset(font, 64, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            fa.name = "NotoSansKR SDF";
            AssetDatabase.CreateAsset(fa, FontAssetPath);
            fa.atlasTextures[0].name = "NotoSansKR SDF Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = "NotoSansKR SDF Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();

            // 다른 씬의 TMP 텍스트에서도 한글이 보이도록 전역 대체 폰트로 등록
            var settings = TMP_Settings.instance;
            if (settings != null)
            {
                var so = new SerializedObject(settings);
                var prop = so.FindProperty("m_fallbackFontAssets");
                bool has = false;
                for (int i = 0; i < prop.arraySize; i++) if (prop.GetArrayElementAtIndex(i).objectReferenceValue == fa) has = true;
                if (!has)
                {
                    prop.InsertArrayElementAtIndex(prop.arraySize);
                    prop.GetArrayElementAtIndex(prop.arraySize - 1).objectReferenceValue = fa;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                }
            }
            return fa;
        }

        static void AddToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));   // 게임 실행 시 로비부터 시작
            if (!scenes.Exists(s => s.path == "Assets/Map/Scenes/MapScene.unity"))
                scenes.Add(new EditorBuildSettingsScene("Assets/Map/Scenes/MapScene.unity", true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif
