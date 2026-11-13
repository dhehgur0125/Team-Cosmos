using System.Collections.Generic;
using UnityEngine;

namespace RVFX.MuzzleFlashesImpacts
{
    [DisallowMultipleComponent]
    public sealed class RandomDecalSpawner : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Assign the MeshRenderer you want to configure (can be on this GameObject or a child).")]
        public MeshRenderer targetRenderer;

        [Header("Random Scale (OnEnable)")]
        [Tooltip("Uniform scale range applied on enable.")]
        public Vector2 uniformScaleRange = new Vector2(0.9f, 1.1f);

        [Header("Random Rotation (Local Z Only)")]
        [Tooltip("Minimum Z rotation in degrees.")]
        public float rotationZMin = 0f;

        [Tooltip("Maximum Z rotation in degrees.")]
        public float rotationZMax = 360f;

        [Header("Materials")]
        public List<Material> materialPool = new List<Material>();

        private void OnEnable()
        {
            targetRenderer.gameObject.SetActive(true);
            ApplyRandomScale();
            ApplyRandomLocalZRotation();
            ApplyRandomMaterial();
        }

        private void ApplyRandomScale()
        {
            if (targetRenderer == null)
                return;

            float min = Mathf.Max(Mathf.Min(uniformScaleRange.x, uniformScaleRange.y), 0.0001f);
            float max = Mathf.Max(uniformScaleRange.x, uniformScaleRange.y);

            float s = Random.Range(min, max);
            targetRenderer.transform.localScale = Vector3.one * s;
        }

        private void ApplyRandomLocalZRotation()
        {
            if (targetRenderer == null)
                return;

            float minZ = Mathf.Min(rotationZMin, rotationZMax);
            float maxZ = Mathf.Max(rotationZMin, rotationZMax);

            float randomZ = Random.Range(minZ, maxZ);

            Transform t = targetRenderer.transform;

            Vector3 euler = t.localEulerAngles;
            euler.z = randomZ;
            t.localEulerAngles = euler;
        }

        private void ApplyRandomMaterial()
        {
            if (targetRenderer == null || materialPool == null || materialPool.Count == 0)
                return;

            Material chosen = null;

            for (int i = 0; i < materialPool.Count; i++)
            {
                var m = materialPool[Random.Range(0, materialPool.Count)];
                if (m != null)
                {
                    chosen = m;
                    break;
                }
            }

            if (chosen != null)
                targetRenderer.sharedMaterial = chosen;
        }
    }
}
