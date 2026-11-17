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
            kind == FarmingItemKind.QuarantineCard || kind == FarmingItemKind.Cutter || kind == FarmingItemKind.PowerModule || kind == FarmingItemKind.FuelCoupler;

        /// <summary>Resources에서 읽어 오는 아이템인지 (주사기·무전기는 대원 프리팹에 직접 연결되어 있음)</summary>
        public static bool IsCatalogItem(FarmingItemKind kind) => kind == FarmingItemKind.Battery || kind == FarmingItemKind.Scanner || IsMissionItem(kind);

        /// <summary>스캔에 표시되는지: 배터리는 직접 찾아야 한다</summary>
        public static bool IsScannable(FarmingItemKind kind) => kind != FarmingItemKind.Battery;

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
                sprite = Resources.Load<Sprite>("FarmingItems/Icons/" + kind);
                Icons[kind] = sprite;
            }
            return sprite;
        }
    }
}
