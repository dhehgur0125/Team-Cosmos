/* [ 코드 설명 ] 배터리·임무 물건을 손에 들었을 때의 모습입니다. 무전기와 같은 자리(1인칭 카메라 앞, 3인칭 손 소켓)에 Resources의 바닥 프리팹 모델을 복제해 보여 줍니다. */
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoidLast.Player
{
    [DisallowMultipleComponent]
    public sealed class FpsCarriedItemView : MonoBehaviour
    {
        sealed class Held { public GameObject firstPerson, world; }

        static readonly Vector3 FirstPersonPosition = new Vector3(0.2f, -0.2f, 0.42f);
        static readonly Vector3 FirstPersonEuler = new Vector3(12, -25, 0);
        // 큰 물건(전력 모듈)이 화면을 가리지 않도록 들고 있을 때만 이 크기 안으로 줄인다 (m)
        const float FirstPersonMaxSize = 0.18f, WorldMaxSize = 0.3f;

        readonly Dictionary<FarmingItemKind, Held> _views = new Dictionary<FarmingItemKind, Held>();
        FpsInventory _inventory;
        FpsLocalVisibility _visibility;
        Transform _firstPersonParent, _worldSocket;
        int _firstPersonLayer;
        Held _shown;

        /// <summary>붙일 위치를 무전기 설정에서 그대로 받는다</summary>
        public void Configure(Transform firstPersonParent, Transform gloveSource, Transform worldSocket)
        {
            _firstPersonParent = firstPersonParent;
            _worldSocket = worldSocket;
            _firstPersonLayer = gloveSource != null ? gloveSource.gameObject.layer : firstPersonParent != null ? firstPersonParent.gameObject.layer : 0;
        }

        void Awake()
        {
            _inventory = GetComponent<FpsInventory>();
            _visibility = GetComponent<FpsLocalVisibility>();
        }

        void LateUpdate()
        {
            if (_inventory == null) return;
            var kind = _inventory.IsAlive ? _inventory.ItemAt(_inventory.SelectedIndex) : FarmingItemKind.None;
            var want = FarmingItems.IsCatalogItem(kind) ? ViewFor(kind) : null;
            if (want == _shown) return;
            Show(_shown, false);
            Show(want, true);
            _shown = want;
        }

        static void Show(Held view, bool on)
        {
            if (view == null) return;
            if (view.firstPerson != null) view.firstPerson.SetActive(on);
            if (view.world != null) view.world.SetActive(on);
        }

        Held ViewFor(FarmingItemKind kind)
        {
            if (_views.TryGetValue(kind, out var view)) return view;
            view = new Held();
            var prefab = FarmingItems.PickupPrefab(kind);
            var model = prefab != null ? prefab.transform.Find("Model") : null;
            if (model != null)
            {
                var fp = new List<Renderer>();
                var world = new List<Renderer>();
                if (_firstPersonParent != null)
                {
                    view.firstPerson = Clone(model, _firstPersonParent, FirstPersonPosition, Quaternion.Euler(FirstPersonEuler), _firstPersonLayer, fp);
                    view.firstPerson.name = kind + " (1P)";
                    Fit(view.firstPerson, FirstPersonMaxSize);
                }
                if (_worldSocket != null)
                {
                    view.world = Clone(model, _worldSocket.parent != null ? _worldSocket.parent : _worldSocket, _worldSocket.localPosition, _worldSocket.localRotation, _worldSocket.gameObject.layer, world);
                    view.world.name = kind + " (3P)";
                    Fit(view.world, WorldMaxSize);
                }
                if (_visibility != null) _visibility.AddRenderers(world.ToArray(), fp.ToArray());
                Show(view, false);
            }
            _views[kind] = view;
            return view;
        }

        static void Fit(GameObject go, float maxSize)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (size > maxSize) go.transform.localScale *= maxSize / size;
        }

        static GameObject Clone(Transform model, Transform parent, Vector3 position, Quaternion rotation, int layer, List<Renderer> renderers)
        {
            var go = Instantiate(model.gameObject, parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.Off; renderers.Add(r); }
            return go;
        }
    }
}
