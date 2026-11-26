// 2인 접속 시험 도우미 (에디터 전용, 게임 빌드에는 들어가지 않음)
// Multiplayer Play Mode의 가상 접속자(Player 2) 창은 직접 조작하기 어려워서,
// 임시 폴더의 명령 파일(%TEMP%/VL_MppmTest/cmd.txt)을 읽어 로비 참가·고유 아이템·준비·다시 참가 등을 대신 누르고,
// 1초마다 상태(clone_status.txt)를 적는다. 가상 접속자(복제 프로젝트)에서만 동작한다.
// 명령 파일이 없으면 아무것도 하지 않는다. 시험이 끝나면 이 파일은 지워도 된다.
#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using VoidLast.Player;
using VoidLast.Monster;

namespace VoidLast.MapTools
{
    [InitializeOnLoad]
    public static class MppmTestDriver
    {
        static readonly string Dir = Path.Combine(Path.GetTempPath(), "VL_MppmTest");
        static readonly string CmdPath = Path.Combine(Dir, "cmd.txt");
        static readonly string StatusPath = Path.Combine(Dir, "clone_status.txt");
        static readonly string LogPath = Path.Combine(Dir, "clone_log.txt");
        static double _nextStatus;
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static bool IsClone => Application.dataPath.Replace('\\', '/').Contains("/Library/VP/");

        static MppmTestDriver()
        {
            if (!IsClone) return;
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (!Directory.Exists(Dir)) return;
            if (type == LogType.Log && !(msg.StartsWith("[") )) return;
            try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + " " + type + " " + msg + (type == LogType.Exception || type == LogType.Error ? "\n  " + stack.Split('\n').FirstOrDefault() : "") + "\n"); } catch { }
        }

        static void Tick()
        {
            if (!Directory.Exists(Dir)) return;
            if (File.Exists(CmdPath))
            {
                string[] lines;
                try { lines = File.ReadAllLines(CmdPath); File.Delete(CmdPath); } catch { return; }
                foreach (var l in lines) if (!string.IsNullOrWhiteSpace(l)) Run(l.Trim());
            }
            if (EditorApplication.timeSinceStartup >= _nextStatus)
            {
                _nextStatus = EditorApplication.timeSinceStartup + 1.0;
                try { File.WriteAllText(StatusPath, Status()); } catch { }
            }
        }

        static void Note(string s) { try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + " CMD " + s + "\n"); } catch { } }

        static object Lobby() => UnityEngine.Object.FindFirstObjectByType(Type.GetType("LobbyManager, Assembly-CSharp"));

        static void CallLobby(string method, params object[] args)
        {
            var lm = Lobby();
            if (lm == null) { Note(method + ": LobbyManager 없음"); return; }
            var m = lm.GetType().GetMethod(method, All);
            if (m == null) { Note(method + ": 메서드 없음"); return; }
            m.Invoke(lm, args);
            Note(method + " 호출");
        }

        static void Run(string line)
        {
            var parts = line.Split(new[] { ' ' }, 2);
            string cmd = parts[0].ToLowerInvariant(), arg = parts.Length > 1 ? parts[1].Trim() : "";
            try
            {
                switch (cmd)
                {
                    case "join":
                    {
                        var lm = Lobby();
                        var input = lm?.GetType().GetField("joinCodeInput", All)?.GetValue(lm) as TMPro.TMP_InputField;
                        if (input != null) input.text = arg;
                        CallLobby("JoinByCodeAsync");
                        break;
                    }
                    case "pick": CallLobby("PickUniqueAsync", (UniqueItemKind)Enum.Parse(typeof(UniqueItemKind), arg)); break;
                    case "ready": CallLobby("ToggleReadyAsync"); break;
                    case "rejoin": CallLobby("RejoinAsync"); break;
                    case "leavegame":   // 게임 도중 나가기 (세션 나가기 + 로비로)
                    {
                        var boot = UnityEngine.Object.FindFirstObjectByType(Type.GetType("Stage2NetworkBootstrap, Assembly-CSharp"));
                        if (boot == null) { Note("Stage2NetworkBootstrap 없음"); break; }
                        boot.GetType().GetMethod("ReturnToLobbyAsync", All).Invoke(boot, new object[] { "시험: 나가기" });
                        Note("ReturnToLobbyAsync 호출");
                        break;
                    }
                    case "tp":
                    {
                        var p = Type.GetType("FpsNetworkPlayer, Assembly-CSharp").GetProperty("Local", All).GetValue(null) as Component;
                        var v = arg.Split(' ').Select(float.Parse).ToArray();
                        var cc = p.GetComponent<CharacterController>();
                        if (cc != null) cc.enabled = false;
                        p.transform.position = new Vector3(v[0], v[1], v[2]);
                        if (cc != null) cc.enabled = true;
                        Note("tp " + arg);
                        break;
                    }
                    case "set":   // set Type.StaticMember value  (bool/int/float/string)
                    {
                        var a = arg.Split(new[] { ' ' }, 2);
                        int dot = a[0].LastIndexOf('.');
                        var t = FindType(a[0].Substring(0, dot));
                        var name = a[0].Substring(dot + 1);
                        var pi = t.GetProperty(name, All);
                        var fi = t.GetField(name, All);
                        var vt = pi != null ? pi.PropertyType : fi.FieldType;
                        object val = Convert.ChangeType(a[1], vt);
                        if (pi != null) pi.SetValue(null, val); else fi.SetValue(null, val);
                        Note("set " + arg);
                        break;
                    }
                    case "invoke":        // invoke 타입 메서드 인자...  (씬의 첫 번째 그 컴포넌트, 또는 static)
                    case "localinvoke":   // localinvoke 타입 메서드 인자...  (내 대원에 붙은 컴포넌트)
                    {
                        var a = arg.Split(' ');
                        var t = FindType(a[0]);
                        object target = null;
                        var methods = t.GetMethods(All).Where(x => x.Name == a[1]).ToArray();
                        var mi = methods.FirstOrDefault(x => x.GetParameters().Count(p => !p.HasDefaultValue) <= a.Length - 2 && x.GetParameters().Length >= a.Length - 2) ?? methods.First();
                        if (!mi.IsStatic)
                        {
                            if (cmd == "localinvoke")
                            {
                                var local = Type.GetType("FpsNetworkPlayer, Assembly-CSharp").GetProperty("Local", All).GetValue(null) as Component;
                                target = local.GetComponentInChildren(t, true);
                            }
                            else target = UnityEngine.Object.FindFirstObjectByType(t);
                        }
                        var ps = mi.GetParameters();
                        var values = new object[ps.Length];
                        for (int i = 0; i < ps.Length; i++)
                        {
                            var pt = ps[i].ParameterType;
                            if (i + 2 < a.Length)
                            {
                                string s = a[i + 2];
                                values[i] = pt.IsEnum ? Enum.Parse(pt, s) : pt == typeof(char) ? (object)s[0] : Convert.ChangeType(s, pt, System.Globalization.CultureInfo.InvariantCulture);
                            }
                            else values[i] = ps[i].HasDefaultValue && ps[i].DefaultValue != null ? ps[i].DefaultValue : (pt.IsValueType ? Activator.CreateInstance(pt) : null);
                        }
                        var ret = mi.Invoke(target, values);
                        Note(line + " → " + (ret ?? "(void)"));
                        break;
                    }
                    case "open":   // 에디터 씬 열기 (플레이 중이 아닐 때)
                        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(arg, UnityEditor.SceneManagement.OpenSceneMode.Single);
                        Note("open " + arg);
                        break;
                    case "load":   // 플레이 중 씬 불러오기
                        UnityEngine.SceneManagement.SceneManager.LoadScene(arg);
                        Note("load " + arg);
                        break;
                    case "click":   // click 버튼글자  (화면에 보이는 버튼 중 글자가 같은 것을 누름)
                    {
                        var hit = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None)
                            .FirstOrDefault(b => b.isActiveAndEnabled && b.GetComponentInChildren<TMPro.TMP_Text>()?.text == arg);
                        if (hit == null) { Note("click: 버튼 없음 '" + arg + "'"); break; }
                        hit.onClick.Invoke();
                        Note("click " + arg);
                        break;
                    }
                    case "netdrop":   // 강제 종료 흉내: 신호 없이 네트워크가 끊긴 것처럼 (방장 쪽엔 30초 동안 유령 대원이 남음)
                    {
                        var nm = NetworkManager.Singleton;
                        var simType = FindType("Unity.Multiplayer.Tools.NetworkSimulator.Runtime.NetworkSimulator");
                        if (nm == null || simType == null) { Note("netdrop: NetworkManager 또는 NetworkSimulator 없음"); break; }
                        var sim = nm.GetComponent(simType) ?? nm.gameObject.AddComponent(simType);
                        simType.GetMethod("Disconnect", All).Invoke(sim, null);
                        Note("netdrop");
                        break;
                    }
                    case "stop": EditorApplication.isPlaying = false; Note("stop"); break;
                    case "play": EditorApplication.isPlaying = true; Note("play"); break;
                    case "clearlog": File.WriteAllText(LogPath, ""); break;
                    default: Note("모르는 명령: " + line); break;
                }
            }
            catch (Exception e) { Note(line + " 실패: " + (e.InnerException ?? e).Message); }
        }

        static Type FindType(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(name);
                if (t != null) return t;
            }
            return null;
        }

        static string Status()
        {
            var sb = new StringBuilder();
            sb.AppendLine("time " + DateTime.Now.ToString("HH:mm:ss") + " playing " + EditorApplication.isPlaying);
            if (!EditorApplication.isPlaying) return sb.ToString();
            sb.AppendLine("scene " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            var nm = NetworkManager.Singleton;
            if (nm != null) sb.AppendLine($"net listening {nm.IsListening} client {nm.IsClient} server {nm.IsServer} connected {nm.IsConnectedClient} localId {nm.LocalClientId} reason '{nm.DisconnectReason}'");
            sb.AppendLine($"balance StartPlayers {GameBalance.StartPlayers} Players {GameBalance.Players} MonsterHealthScale {GameBalance.MonsterHealthScale} HoldoutScale {GameBalance.HoldoutScale} SyncWindow {GameBalance.SyncWindow}");
            sb.AppendLine("unique " + UniqueItems.LocalChoice + " token " + Rejoin.Token + " ticket " + (Rejoin.TryLoadTicket(out var tk) ? tk.code + "/" + tk.players : "none"));
            var lm = Lobby() as MonoBehaviour;
            if (lm != null)
            {
                var room = lm.GetType().GetField("roomPanel", All).GetValue(lm) as GameObject;
                var status = lm.GetType().GetField("statusText", All).GetValue(lm) as TMPro.TMP_Text;
                var rj = lm.GetType().GetField("_rejoinButton", All)?.GetValue(lm) as GameObject;
                var hint = lm.GetType().GetField("startHintText", All).GetValue(lm) as TMPro.TMP_Text;
                var ongoing = lm.GetType().GetField("_ongoingRoom", All)?.GetValue(lm) as GameObject;
                sb.AppendLine($"lobby room {(room != null && room.activeInHierarchy)} rejoinBtn {(rj != null && rj.activeInHierarchy)} ongoingRoom {(ongoing != null)} status '{status?.text}' hint '{hint?.text}'");
            }
            sb.AppendLine("loading " + LoadingScreen.IsShowing);
            try
            {
                var vx = Unity.Services.Vivox.VivoxService.Instance;
                if (vx != null && vx.IsLoggedIn)
                {
                    var vlist = new System.Collections.Generic.List<string>();
                    foreach (var kv in vx.ActiveChannels) vlist.Add(kv.Key + "(" + kv.Value.Count + ")");
                    sb.AppendLine("voice key " + (VoiceChatService.Exists ? VoiceChatService.Instance.SessionKey : "-") + " channels " + string.Join(",", vlist) + " tx " + string.Join(",", vx.TransmittingChannels) + " mic " + (VoiceChatService.Exists && VoiceChatService.Instance.MicMuted ? "off" : "on"));
                }
                else sb.AppendLine("voice loggedOut");
            }
            catch (Exception e) { sb.AppendLine("voice error " + e.Message); }
            var ft = FloorTransition.Instance;
            if (ft != null)
            {
                var prompt = typeof(FloorTransition).GetField("_prompt", All)?.GetValue(ft) as TMPro.TMP_Text;
                sb.AppendLine($"floor {ft.floor} moving {ft.IsMoving} inCutscene {FloorTransition.InCutscene} cleared {string.Join(",", FloorCarryOver.ClearedFloors)} prompt '{prompt?.text}'");
            }
            var local = FpsNetworkPlayer.Local;
            foreach (var p in FpsNetworkPlayer.All)
            {
                if (p == null) continue;
                var h = p.GetComponent<FpsHealth>();
                var m = p.GetComponent<FpsMotor>();
                var u = p.GetComponent<FpsUniqueItem>();
                sb.AppendLine($"player owner {p.OwnerClientId}{(p == local ? " (me)" : "")} pos {p.transform.position.ToString("F2")} rotY {p.transform.eulerAngles.y:0} hp {(h != null ? h.Current : -1)} stamina {(m != null ? m.Stamina : -1):0.00} unique {(u != null ? u.Kind.ToString() : "-")}");
            }
            foreach (var mh in UnityEngine.Object.FindObjectsByType<MonsterHealth>(FindObjectsSortMode.None))
            {
                var wake = mh.GetComponent<MonsterWakeWarning>();
                var active = wake != null && (bool)typeof(MonsterWakeWarning).GetField("_active", All).GetValue(wake);
                sb.AppendLine($"monster {mh.name} hp {mh.Current:0} groggy {mh.GroggyRemaining:0} wakeWarning {active} pos {mh.transform.position.ToString("F1")}");
            }
            foreach (var tx in UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None))
                if (tx.isActiveAndEnabled && tx.alpha > 0.05f && !string.IsNullOrEmpty(tx.text) && (tx.text.Contains("방송") || tx.text.Contains("침입") || tx.text.Contains("도움말")))
                    sb.AppendLine("hud '" + (tx.text.Length > 70 ? tx.text.Substring(0, 70) : tx.text).Replace("\n", " / ") + "'");
            var picks = UnityEngine.Object.FindObjectsByType<FpsWorldPickup>(FindObjectsSortMode.None);
            sb.AppendLine("pickups " + string.Join(", ", picks.GroupBy(x => x.Item).Select(g => g.Key + "=" + g.Count())));
            return sb.ToString();
        }
    }
}
#endif
