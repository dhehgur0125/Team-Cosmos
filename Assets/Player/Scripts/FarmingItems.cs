/* [ 코드 설명 ] 파밍 아이템 종류별 정보를 한곳에 모읍니다. 주사기·무전기를 뺀 아이템의 바닥 프리팹과 핫바 아이콘은 Resources/FarmingItems에서 읽습니다. */
using System.Collections.Generic;
using UnityEngine;

namespace VoidLast.Player
{
    public static class FarmingItems
    {
        static readonly Dictionary<FarmingItemKind, FpsWorldPickup> Pickups = new Dictionary<FarmingItemKind, FpsWorldPickup>();
        static readonly Dictionary<FarmingItemKind, Sprite> Icons = new Dictionary<FarmingItemKind, Sprite>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Pickups.Clear(); Icons.Clear(); }

        /// <summary>임무 물건: 그 층에서만 쓰고 아래층으로 가져가지 않는다</summary>
        public static bool IsMissionItem(FarmingItemKind kind) =>
            kind == FarmingItemKind.QuarantineCard || kind == FarmingItemKind.Cutter || kind == FarmingItemKind.PowerModule || kind == FarmingItemKind.FuelCoupler ||
            kind == FarmingItemKind.AdminCard || kind == FarmingItemKind.ToolKit || kind == FarmingItemKind.PowerCell;

        /// <summary>무거운 물건: 한 대원이 하나만 들 수 있고, 들고 있는 동안 달리기·사격을 할 수 없다</summary>
        public static bool IsHeavy(FarmingItemKind kind) => kind == FarmingItemKind.PowerCell;

        /// <summary>Resources에서 읽어 오는 아이템인지 (주사기·무전기는 대원 프리팹에 직접 연결되어 있음)</summary>
        public static bool IsCatalogItem(FarmingItemKind kind) => kind == FarmingItemKind.Battery || kind == FarmingItemKind.Scanner || kind == FarmingItemKind.AmmoPack || IsMap(kind) || IsMissionItem(kind);

        /// <summary>스캔에 표시되는지: 배터리는 직접 찾아야 한다</summary>
        public static bool IsScannable(FarmingItemKind kind) => kind != FarmingItemKind.None;

        public static int MapFloor(FarmingItemKind kind) => kind == FarmingItemKind.Map ? 2 :
            kind == FarmingItemKind.MapFloor1 ? 1 : kind == FarmingItemKind.MapFloor3 ? 3 :
            kind == FarmingItemKind.MapFloor4 ? 4 : kind == FarmingItemKind.MapFloor5 ? 5 : 0;
        public static bool IsMap(FarmingItemKind kind) => MapFloor(kind) > 0;
        public static bool CanLootOnFloor(FarmingItemKind kind, string sceneName)
            => !IsMap(kind) || sceneName == "Stage2_Ship_" + MapFloor(kind) + "f";
        public static string HudLabel(FarmingItemKind kind)
        {
            switch (kind)
            {
                case FarmingItemKind.Syringe: return "SYRINGE";
                case FarmingItemKind.Radio: return "RADIO";
                case FarmingItemKind.Battery: return "BATTERY";
                case FarmingItemKind.QuarantineCard: return "QUARANTINE CARD";
                case FarmingItemKind.Cutter: return "CUTTER";
                case FarmingItemKind.PowerModule: return "POWER MODULE";
                case FarmingItemKind.FuelCoupler: return "FUEL COUPLER";
                case FarmingItemKind.Scanner: return "SCANNER";
                case FarmingItemKind.AdminCard: return "ADMIN CARD";
                case FarmingItemKind.ToolKit: return "TOOL KIT";
                case FarmingItemKind.PowerCell: return "POWER CELL";
                case FarmingItemKind.AmmoPack: return "AMMO PACK";
                case FarmingItemKind.Map: return "건물도면 · 2층";
                case FarmingItemKind.MapFloor1: return "건물도면 · 1층";
                case FarmingItemKind.MapFloor3: return "건물도면 · 3층";
                case FarmingItemKind.MapFloor4: return "건물도면 · 4층";
                case FarmingItemKind.MapFloor5: return "건물도면 · 5층";
                default: return "ITEM";
            }
        }

        public static FpsWorldPickup PickupPrefab(FarmingItemKind kind)
        {
            if (!IsCatalogItem(kind)) return null;
            if (!Pickups.TryGetValue(kind, out var prefab))
            {
                var go = Resources.Load<GameObject>("FarmingItems/" + kind);
                prefab = go != null ? go.GetComponent<FpsWorldPickup>() : null;
                Pickups[kind] = prefab;
            }
            return prefab;
        }

        public static Sprite Icon(FarmingItemKind kind)
        {
            if (!IsCatalogItem(kind)) return null;
            if (!Icons.TryGetValue(kind, out var sprite))
            {
                sprite = Resources.Load<Sprite>("FarmingItems/Icons/" + (IsMap(kind) ? FarmingItemKind.Map : kind));
                Icons[kind] = sprite;
            }
            return sprite;
        }
    }
}
