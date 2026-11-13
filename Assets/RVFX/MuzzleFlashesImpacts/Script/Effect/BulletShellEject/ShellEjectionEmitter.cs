using UnityEngine;

namespace RVFX.MuzzleFlashesImpacts
{
    [DisallowMultipleComponent]
    public sealed class ShellEjectionEmitter : MonoBehaviour
    {
        [SerializeField] private ParticleSystem ps;

        private void Reset()
        {
            ps = GetComponent<ParticleSystem>();
        }

        private void Awake()
        {
            if (!ps)
                ps = GetComponent<ParticleSystem>();

            if (!ps)
            {
                Debug.LogError($"{nameof(ShellEjectionEmitter)} requires a ParticleSystem.");
                enabled = false;
            }
        }

        private void Update()
        {
        }


        public void Spawn()
        {
            if (!ps) return;
            ps.Emit(1);
        }
    }
}
