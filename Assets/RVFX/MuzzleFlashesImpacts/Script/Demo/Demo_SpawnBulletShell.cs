using System.Collections.Generic;
using UnityEngine;

namespace RVFX.MuzzleFlashesImpacts
{
    public class Demo_SpawnBulletShell : MonoBehaviour
    {
        public List<ShellEjectionEmitter> emitters = new List<ShellEjectionEmitter>();

        [Min(0.01f)]
        public float spawnGapTime = 0.15f;

        public bool playOnEnable = true;

        private float _nextSpawnTime;
        private bool _isPlaying;

        void OnEnable()
        {
            _isPlaying = playOnEnable;
            ScheduleNextSpawn();
        }

        void Update()
        {
            if (!_isPlaying)
                return;

            if (emitters == null || emitters.Count == 0)
                return;

            if (Time.time < _nextSpawnTime)
                return;

            for (int i = 0; i < emitters.Count; i++)
            {
                var emitter = emitters[i];
                if (emitter == null)
                    continue;

                emitter.Spawn();
            }

            ScheduleNextSpawn();
        }

        public void Play()
        {
            _isPlaying = true;
            ScheduleNextSpawn();
        }

        public void Stop()
        {
            _isPlaying = false;
        }

        public void TriggerOnce()
        {
            if (emitters == null)
                return;

            for (int i = 0; i < emitters.Count; i++)
            {
                var emitter = emitters[i];
                if (emitter == null)
                    continue;

                emitter.Spawn();
            }
        }

        private void ScheduleNextSpawn()
        {
            _nextSpawnTime = Time.time + Mathf.Max(0.01f, spawnGapTime);
        }
    }
}
