using UnityEngine;

namespace RVFX.MuzzleFlashesImpacts
{
    public class EffectConfigHelper : MonoBehaviour
    {
        [Header("Hue Shift (CustomData1.X / _HueShift)")]
        [Range(-180f, 180f)]
        public float hueShift = 0f;

        public bool includeInactive = true;
        public bool updateEveryFrame = false;


        private void OnEnable()
        {
            ApplyHueShift();
        }

        private void Update()
        {
            if (updateEveryFrame)
            {
                ApplyHueShift();
            }
        }
        

        public void ApplyHueShift()
        {
            var pss = GetComponentsInChildren<ParticleSystem>(includeInactive);
            if (pss != null && pss.Length > 0)
            {
                var curve = new ParticleSystem.MinMaxCurve(hueShift);
                for (int i = 0; i < pss.Length; i++)
                {
                    var ps = pss[i];
                    if (ps == null) continue;
                    var custom = ps.customData;
                    custom.SetVector(ParticleSystemCustomData.Custom1, 0, curve);
                }
            }

            var emitters = GetComponentsInChildren<RVFX.Tools.BulletTrailEmitter>(includeInactive);
            if (emitters == null || emitters.Length == 0)
                return;

            for (int i = 0; i < emitters.Length; i++)
            {
                var e = emitters[i];
                if (e == null) continue;
                e.SetHueShift(hueShift, includeInactive);
            }
        }
    }
}
