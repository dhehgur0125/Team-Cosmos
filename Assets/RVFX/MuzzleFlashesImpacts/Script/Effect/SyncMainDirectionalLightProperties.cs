using System.Collections.Generic;
using UnityEngine;

namespace RVFX.MuzzleFlashesImpacts
{
    [DisallowMultipleComponent]
    public sealed class SyncMainDirectionalLightProperties : MonoBehaviour
    {
        [Header("Targets")]
        public List<ParticleSystem> particleSystems = new List<ParticleSystem>();

        [Header("Shader Properties")]
        public string lightColorProperty = "_LightColor";
        public string lightIntensityProperty = "_LightIntensity";

        [Header("Update Mode")]
        [Tooltip("If enabled, checks every frame but only writes when values changed.")]
        public bool updateEveryFrame = false;

        [Tooltip("If enabled, de-duplicates shared materials so the same material is written only once per apply.")]
        public bool dedupeMaterials = true;

        // Cached last values to avoid useless writes
        private bool hasLast;
        private Color lastColor;
        private float lastIntensity;

        // Reusable set to avoid GC
        private readonly HashSet<Material> materialSet = new HashSet<Material>(64);

        private void Awake()
        {
            Apply(force: true);
        }

        private void Update()
        {
            if (updateEveryFrame)
                Apply(force: false);
        }

        [ContextMenu("Apply Now")]
        public void ApplyNow()
        {
            Apply(force: true);
        }

        private void Apply(bool force)
        {
            Light sun = RenderSettings.sun;
            if (!sun) return;

            Color c = sun.color;
            float intensity = sun.intensity;

            // Dirty check: skip if nothing changed
            if (!force && hasLast)
            {
                // Color equality is exact; if you want tolerance, tell me and I'll add epsilon.
                if (c == lastColor && Mathf.Approximately(intensity, lastIntensity))
                    return;
            }

            hasLast = true;
            lastColor = c;
            lastIntensity = intensity;

            if (particleSystems == null || particleSystems.Count == 0)
                return;

            if (dedupeMaterials)
                materialSet.Clear();

            for (int i = 0; i < particleSystems.Count; i++)
            {
                var ps = particleSystems[i];
                if (!ps) continue;

                var psr = ps.GetComponent<ParticleSystemRenderer>();
                if (!psr) continue;

                var mats = psr.sharedMaterials;
                if (mats == null || mats.Length == 0) continue;

                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (!mat) continue;

                    if (dedupeMaterials && !materialSet.Add(mat))
                        continue; // already written this material this apply

                    if (!string.IsNullOrEmpty(lightColorProperty) && mat.HasProperty(lightColorProperty))
                        mat.SetColor(lightColorProperty, c);

                    if (!string.IsNullOrEmpty(lightIntensityProperty) && mat.HasProperty(lightIntensityProperty))
                        mat.SetFloat(lightIntensityProperty, intensity);
                }
            }
        }
    }
}
