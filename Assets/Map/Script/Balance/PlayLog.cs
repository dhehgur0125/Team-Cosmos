using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using VoidLast.Player;

/// <summary>
/// 판 기록 (밸런스 시험용). 층을 떠날 때·게임이 끝날 때 한 줄씩 CSV 파일에 쓴다.
/// 파일: Application.persistentDataPath/PlayLogs/VOIDLAST_날짜_시각.csv (한 판 = 한 파일, 엑셀로 열 수 있음)
///  - 내 대원: 받은 피해, 죽음, 쏜 총알, 주운·쓴 탄알집과 주사기 (대원마다 자기 PC에 남는다)
///  - 방장(또는 혼자 실행): 몬스터 종류별로 대원에게 준 피해, 쓰러진 횟수 (몬스터는 방장 PC에서 돌기 때문)
/// GameBalance.writePlayLogs를 끄면 쓰지 않는다.
/// </summary>
public static class PlayLog
{
    sealed class FloorRecord
    {
        public int floor;
        public float start;
        public float damageTaken;
        public int deaths, shots, ammoPicked, ammoUsed, syringePicked, syringeUsed;
        public readonly Dictionary<GameBalance.MonsterKind, float> monsterDamage = new Dictionary<GameBalance.MonsterKind, float>();
        public readonly Dictionary<GameBalance.MonsterKind, int> monsterDowns = new Dictionary<GameBalance.MonsterKind, int>();
    }

    static FloorRecord _floor;
    static string _file;
    static GameObject _tracked;
    static FpsHealth _health;
    static FpsPistol _pistol;
    static FpsInventory _inventory;
    static bool _pickupHooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { _floor = null; _file = null; Untrack(); _pickupHooked = false; }

    static bool Enabled => GameBalance.Current.writePlayLogs;
    public static string Folder => Path.Combine(Application.persistentDataPath, "PlayLogs");

    /// <summary>로비에서: 다음 게임은 새 파일에 쓴다</summary>
    public static void NewGame() { _file = null; _floor = null; }

    /// <summary>층에 도착했을 때 (FloorTransition)</summary>
    public static void BeginFloor(int floor)
    {
        _floor = new FloorRecord { floor = floor, start = Time.time };
        if (!_pickupHooked) { FpsWorldPickup.Collected += OnCollected; _pickupHooked = true; }
    }

    /// <summary>이 대원의 사격·피해·아이템을 센다 (내 대원 하나만)</summary>
    public static void Track(GameObject player)
    {
        if (player == null || player == _tracked) return;
        Untrack();
        _tracked = player;
        _health = player.GetComponent<FpsHealth>();
        _pistol = player.GetComponent<FpsPistol>();
        _inventory = player.GetComponent<FpsInventory>();
        if (_health != null) { _health.DamageTaken += OnDamage; _health.Died += OnDied; }
        if (_pistol != null) _pistol.Shot += OnShot;
    }

    static void Untrack()
    {
        if (_health != null) { _health.DamageTaken -= OnDamage; _health.Died -= OnDied; }
        if (_pistol != null) _pistol.Shot -= OnShot;
        _tracked = null; _health = null; _pistol = null; _inventory = null;
    }

    static void OnDamage(float amount) { if (_floor != null) _floor.damageTaken += amount; }
    static void OnDied() { if (_floor != null) _floor.deaths++; }
    static void OnShot() { if (_floor != null) _floor.shots++; }

    static void OnCollected(FpsWorldPickup pickup, FpsInventory inventory)
    {
        if (_floor == null || pickup == null || inventory == null || inventory != _inventory) return;
        if (pickup.Item == FarmingItemKind.AmmoPack) _floor.ammoPicked++;
        else if (pickup.Item == FarmingItemKind.Syringe) _floor.syringePicked++;
    }

    /// <summary>내 대원이 탄알집·주사기를 다 썼을 때 (FpsInventory)</summary>
    public static void ItemUsed(FpsInventory inventory, FarmingItemKind kind)
    {
        if (_floor == null || inventory == null || inventory != _inventory) return;
        if (kind == FarmingItemKind.AmmoPack) _floor.ammoUsed++;
        else if (kind == FarmingItemKind.Syringe) _floor.syringeUsed++;
    }

    /// <summary>방장: 몬스터가 대원을 때렸을 때</summary>
    public static void MonsterHit(GameObject monster, float amount)
    {
        if (_floor == null || amount <= 0) return;
        var kind = GameBalance.KindOf(monster);
        _floor.monsterDamage.TryGetValue(kind, out float v);
        _floor.monsterDamage[kind] = v + amount;
    }

    /// <summary>방장: 몬스터가 쓰러졌을 때</summary>
    public static void MonsterDowned(GameBalance.MonsterKind kind)
    {
        if (_floor == null) return;
        _floor.monsterDowns.TryGetValue(kind, out int v);
        _floor.monsterDowns[kind] = v + 1;
    }

    /// <summary>층을 떠날 때 / 게임이 끝날 때: 이 층 기록을 파일에 한 줄 쓴다</summary>
    public static void EndFloor(string result)
    {
        var f = _floor;
        _floor = null;
        if (f == null || !Enabled) return;
        try
        {
            Directory.CreateDirectory(Folder);
            bool header = false;
            if (string.IsNullOrEmpty(_file))
            {
                _file = Path.Combine(Folder, "VOIDLAST_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                header = true;
            }
            var sb = new StringBuilder();
            var kinds = new[] { GameBalance.MonsterKind.Small, GameBalance.MonsterKind.Frog, GameBalance.MonsterKind.Tentacle, GameBalance.MonsterKind.EyedLarge, GameBalance.MonsterKind.BlindLarge };
            if (header)
            {
                sb.Append("시각,층,인원,걸린 시간(분),결과,받은 피해,죽음,쏜 총알,주운 탄알집,쓴 탄알집,주운 주사기,쓴 주사기");
                foreach (var k in kinds) sb.Append(",피해:" + GameBalance.KindName(k));
                foreach (var k in kinds) sb.Append(",쓰러뜨림:" + GameBalance.KindName(k));
                sb.AppendLine();
            }
            sb.Append(DateTime.Now.ToString("HH:mm:ss")).Append(',').Append(f.floor).Append(',').Append(GameBalance.Players).Append(',')
              .Append(((Time.time - f.start) / 60f).ToString("0.0")).Append(',').Append(result).Append(',')
              .Append(Mathf.RoundToInt(f.damageTaken)).Append(',').Append(f.deaths).Append(',').Append(f.shots).Append(',')
              .Append(f.ammoPicked).Append(',').Append(f.ammoUsed).Append(',').Append(f.syringePicked).Append(',').Append(f.syringeUsed);
            foreach (var k in kinds) { f.monsterDamage.TryGetValue(k, out float d); sb.Append(',').Append(Mathf.RoundToInt(d)); }
            foreach (var k in kinds) { f.monsterDowns.TryGetValue(k, out int d); sb.Append(',').Append(d); }
            sb.AppendLine();
            // 엑셀에서 한글이 깨지지 않도록 처음 쓸 때 BOM을 붙인다
            File.AppendAllText(_file, sb.ToString(), new UTF8Encoding(header));
            Debug.Log("[PlayLog] " + _file + " ← " + f.floor + "층 " + result);
        }
        catch (Exception e) { Debug.LogWarning("[PlayLog] 기록을 쓰지 못함: " + e.Message); }
    }
}
