using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RVFX.MuzzleFlashesImpacts
{
    [System.Serializable]
    public class RepeatTarget
    {
        public GameObject target;

        [Header("Timing (Seconds)")]
        public Vector2 intervalRange = new Vector2(0.8f, 1.5f);

        [Header("Random Local Position")]
        public Vector2 localOffsetXRange = new Vector2(-0.1f, 0.1f);
        public Vector2 localOffsetYRange = new Vector2(-0.1f, 0.1f);

        [HideInInspector] public Vector3 baseLocalPosition;
        [HideInInspector] public Coroutine routine;
    }

    public sealed class Demo_RepeatToggle : MonoBehaviour
    {
        [Header("Targets")]
        public List<RepeatTarget> targets = new List<RepeatTarget>();

        private void OnEnable()
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].target == null)
                    continue;

                targets[i].baseLocalPosition = targets[i].target.transform.localPosition;
                targets[i].routine = StartCoroutine(TargetLoop(targets[i]));
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].routine != null)
                {
                    StopCoroutine(targets[i].routine);
                    targets[i].routine = null;
                }
            }
        }

        private IEnumerator TargetLoop(RepeatTarget data)
        {
            while (true)
            {
                float wait = GetRandomInterval(data.intervalRange);
                yield return new WaitForSeconds(wait);

                ApplyRandomLocalOffset(data);

                if (data.target != null)
                {
                    data.target.SetActive(false);
                    data.target.SetActive(true);
                }
            }
        }

        private void ApplyRandomLocalOffset(RepeatTarget data)
        {
            if (data.target == null)
                return;

            float x = Random.Range(
                Mathf.Min(data.localOffsetXRange.x, data.localOffsetXRange.y),
                Mathf.Max(data.localOffsetXRange.x, data.localOffsetXRange.y)
            );

            float y = Random.Range(
                Mathf.Min(data.localOffsetYRange.x, data.localOffsetYRange.y),
                Mathf.Max(data.localOffsetYRange.x, data.localOffsetYRange.y)
            );

            data.target.transform.localPosition =
                data.baseLocalPosition + new Vector3(x, y, 0f);
        }

        private float GetRandomInterval(Vector2 range)
        {
            float min = Mathf.Max(Mathf.Min(range.x, range.y), 0f);
            float max = Mathf.Max(range.x, range.y);

            return Random.Range(min, max);
        }
    }
}