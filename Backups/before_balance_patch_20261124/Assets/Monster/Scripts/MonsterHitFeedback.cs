using UnityEngine;
using UnityEngine.SceneManagement;
namespace VoidLast.Monster
{
    [DisallowMultipleComponent, RequireComponent(typeof(MonsterHealth))]
    public sealed class MonsterHitFeedback : MonoBehaviour
    {
        [SerializeField] GameObject bloodPrefab;
        [SerializeField, Range(0.05f, 1f)] float effectScale = 0.48f;
        const int PoolSize = 3;
        readonly GameObject[] pool = new GameObject[PoolSize];
        readonly ParticleSystem[][] systems = new ParticleSystem[PoolSize][];
        readonly float[] expiry = new float[PoolSize];
        MonsterHealth health;
        int next;
        public int BurstsPlayed { get; private set; }
        void Awake()
        {
            health = GetComponent<MonsterHealth>();
            if (bloodPrefab == null) return;
            for (int i=0;i<PoolSize;i++) {
                pool[i] = Instantiate(bloodPrefab);
                pool[i].name = name + "_BloodHit_" + i;
                SceneManager.MoveGameObjectToScene(pool[i], gameObject.scene);
                pool[i].transform.localScale = Vector3.one * effectScale;
                systems[i] = pool[i].GetComponentsInChildren<ParticleSystem>(true);
                foreach(var p in systems[i]) p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                pool[i].SetActive(false);
            }
        }
        void OnEnable() { health.HitConfirmed += PlayBlood; }
        void OnDisable()
        {
            health.HitConfirmed -= PlayBlood;
            foreach(var item in pool) if(item != null) item.SetActive(false);
        }
        public void PlayBlood(Vector3 point, Vector3 outward)
        {
            if(!isActiveAndEnabled || pool[next] == null) return;
            if(!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)) return;
            var item=pool[next];
            item.SetActive(true);
            Vector3 direction = outward.sqrMagnitude > 0.001f ? outward.normalized : Vector3.up;
            item.transform.SetPositionAndRotation(point + direction * 0.08f, Quaternion.LookRotation(direction));
            foreach(var p in systems[next]) { p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); p.Play(false); }
            expiry[next] = Time.time + 1.5f;
            next = (next+1)%PoolSize;
            BurstsPlayed++;
        }
        void Update()
        {
            for(int i=0;i<PoolSize;i++) if(pool[i]!=null && pool[i].activeSelf && Time.time>=expiry[i]) pool[i].SetActive(false);
        }
        void OnDestroy() { foreach(var item in pool) if(item!=null) Destroy(item); }
    }
}