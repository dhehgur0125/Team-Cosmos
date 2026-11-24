/* [ 코드 설명 ] 몬스터 테스트 씬 전용 확인용 오버레이입니다. 플레이어 HP, 쪼꼬미와 눈 없는 몬스터의 현재 상태, 조작 키를 화면 오른쪽 위에 보여주고 Y 키로 플레이어 체력을 되돌립니다. 실제 게임 UI가 아니며 Editor/Development Build에서만 동작합니다. */
using System.Reflection;
using System.Text;
using UnityEngine;
using VoidLast.Player;

namespace VoidLast.Monster
{
    public sealed class MonsterTestHud : MonoBehaviour
    {
        [SerializeField] KeyCode reviveKey = KeyCode.Y;
        [SerializeField, Min(0.05f)] float refreshInterval = 0.2f;
        [SerializeField, Min(0.5f)] float uiScale = 1.3f;

        readonly StringBuilder text = new StringBuilder();
        GUIStyle style;
        float nextRefresh;
        int lineCount;

        void Awake()
        {
            // 릴리스 빌드에서는 꺼 둔다
            if (!Application.isEditor && !Debug.isDebugBuild) enabled = false;
        }

        void Update()
        {
            if (Input.GetKeyDown(reviveKey)) Revive();
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + refreshInterval;
            Rebuild();
        }

        void Rebuild()
        {
            text.Clear();
            lineCount = 0;

            var player = FindFirstObjectByType<FpsHealth>();
            if (player == null) Line("<color=#ff6666>플레이어(FpsHealth)가 없습니다</color>");
            else if (player.IsDead) Line("<color=#ff4444><b>플레이어 사망</b></color>  (" + reviveKey + ": 부활)");
            else
            {
                float ratio = player.Current / player.Maximum;
                string color = ratio > 0.5f ? "#7CFC7C" : ratio > 0.25f ? "#FFD84A" : "#FF5555";
                Line("<b>HP</b> <color=" + color + ">" + Mathf.CeilToInt(player.Current) + " / " + Mathf.CeilToInt(player.Maximum) + "</color>  (" + reviveKey + ": 체력 복구)");
            }

            Line("<color=#9fe8ff>Z</color> 스캔 : 눈 없는 몬스터를 부름");
            Line("<color=#888888>────────────────</color>");

            int index = 1;
            foreach (var monster in FindObjectsByType<SmallMonsterAI>())
                Line("쪼꼬미 #" + index++ + "  <b>" + monster.State + "</b>" + (monster.IsAggro ? "  <color=#ff8888>(어그로)</color>" : ""));
            index = 1;
            foreach (var monster in FindObjectsByType<BlindMonsterAI>())
                Line("눈 없는 애 #" + index++ + "  <b>" + monster.State + "</b>");
            if (index == 1 && lineCount <= 3) Line("<color=#888888>씬에 몬스터가 없습니다</color>");
        }

        void Line(string value)
        {
            if (lineCount > 0) text.Append('\n');
            text.Append(value);
            lineCount++;
        }

        void Revive()
        {
            var player = FindFirstObjectByType<FpsHealth>();
            if (player == null) return;

            if (!player.IsDead) { player.Heal(player.Maximum - player.Current); return; }

            // FpsHealth에는 부활 API가 없어서, 확인용으로만 내부 값을 직접 되돌린다
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(FpsHealth).GetField("<Current>k__BackingField", flags)?.SetValue(player, player.Maximum);
            typeof(FpsHealth).GetField("deathHandled", flags)?.SetValue(player, false);
        }

        void OnGUI()
        {
            if (lineCount == 0) return;
            if (style == null) style = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14, normal = { textColor = Color.white } };

            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

            float width = 330f;
            float height = 14f + lineCount * 21f;
            var box = new Rect(Screen.width / uiScale - width - 12f, 12f, width, height);
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(box.x + 10f, box.y + 6f, width - 20f, height - 8f), text.ToString(), style);

            GUI.matrix = oldMatrix;
        }
    }
}
