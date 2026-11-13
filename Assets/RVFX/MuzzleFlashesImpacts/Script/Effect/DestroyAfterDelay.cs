using UnityEngine;

namespace RVFX.MuzzleFlashesImpacts
{
    public sealed class DestroyAfterDelay : MonoBehaviour
    {
        [Min(0f)]
        [Tooltip("Seconds before this GameObject is destroyed.")]
        public float delay = 2f;

        private float timer;

        private void OnEnable()
        {
            timer = 0f;
        }

        private void Update()
        {
            timer += Time.deltaTime;

            if (timer >= delay)
            {
                Destroy(gameObject);
            }
        }
    }
}