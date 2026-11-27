using UnityEngine;
namespace VoidLast.Player
{
    public static class FpsBattery
    {
        public static bool UsesNetwork(GameObject owner)
        {
            var kind=FpsUniqueItem.For(owner).Kind;
            if(kind==UniqueItemKind.Stun){var net=owner.GetComponent<FpsFlashEmitterNetwork>();return net!=null&&net.IsSpawned;}
            if(kind==UniqueItemKind.Healer){var net=owner.GetComponent<FpsMedicalKitNetwork>();return net!=null&&net.IsSpawned;}
            return false;
        }
        public static bool CanRecharge(GameObject owner, uint requestId = 0)
        {
            switch(FpsUniqueItem.For(owner).Kind)
            {
                case UniqueItemKind.Scanner: var detector=owner.GetComponent<ItemDetector>(); return detector!=null && detector.ScannerUsesRemaining<detector.scannerMaxUses;
                case UniqueItemKind.Stun: var stun=owner.GetComponent<FpsFlashEmitter>();return stun!=null && stun.RemainingUses<stun.maxUses;
                case UniqueItemKind.Healer:var heal=owner.GetComponent<FpsMedicalKit>();return heal!=null && heal.RemainingUses<heal.MaximumUses;
                default:return false;
            }
        }
        public static bool Recharge(GameObject owner, uint requestId = 0)
        {
            if(!CanRecharge(owner))return false;
            switch(FpsUniqueItem.For(owner).Kind)
            {
                case UniqueItemKind.Scanner: owner.GetComponent<ItemDetector>().RechargeScanner();return true;
                case UniqueItemKind.Stun:
                    var net=owner.GetComponent<FpsFlashEmitterNetwork>();
                    return net!=null && net.IsSpawned ? net.RequestBatteryRecharge(requestId) : owner.GetComponent<FpsFlashEmitter>().RechargeOffline();
                case UniqueItemKind.Healer:
                    var heal=owner.GetComponent<FpsMedicalKitNetwork>();
                    return heal!=null && heal.IsSpawned ? heal.RequestBatteryRecharge(requestId) : owner.GetComponent<FpsMedicalKit>().RechargeOffline();
                default:return false;
            }
        }
    }
}
