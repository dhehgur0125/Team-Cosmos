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
        // 탄약 팩: 구급상자처럼 윗 손잡이를 쥐고 오른쪽 아래에 든다 (원점 = 상자 바닥 가운데).
        // 사용하면 몸쪽(아래 가운데)으로 끌어당겨 기울인 뒤, 끝나면 원래 자리로 돌아온다
        public static readonly Vector3 AmmoHeldPosition = new Vector3(0.3f, -0.29f, 0.5f);
        public static readonly Vector3 AmmoHeldEuler = new Vector3(350, 340, 0);
        static readonly Vector3 AmmoUsePosition = new Vector3(0.18f, -0.36f, 0.4f);
        static readonly Vector3 AmmoUseEuler = new Vector3(25, 350, -20);
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
            if (want != _shown)
            {
                Show(_shown, false);
                Show(want, true);
                _shown = want;
            }
            if (want != null && want.firstPerson != null && kind == FarmingItemKind.AmmoPack) PoseAmmoPack(want.firstPerson.transform);
        }

        /// <summary>1인칭으로 들고 있는 모델 (FpsItemHands가 손을 붙일 때 사용). 아직 만들지 않았으면 만든다.</summary>
        public Transform FirstPersonModel(FarmingItemKind kind) => FarmingItems.IsCatalogItem(kind) ? ViewFor(kind)?.firstPerson?.transform : null;

        // Pull in quickly, hold against the body with a slight press, return at the very end.
        void PoseAmmoPack(Transform held)
        {
            float t = _inventory.UsingItem == FarmingItemKind.AmmoPack ? _inventory.UseProgress : 0;
            float blend = t < 0.2f ? Mathf.SmoothStep(0, 1, t / 0.2f) : t < 0.85f ? 1 : Mathf.SmoothStep(1, 0, (t - 0.85f) / 0.15f);
            float press = t > 0.2f && t < 0.85f ? Mathf.Sin((t - 0.2f) / 0.65f * Mathf.PI * 3) * 0.008f : 0;
            held.localPosition = Vector3.Lerp(AmmoHeldPosition, AmmoUsePosition, blend) + new Vector3(0, 0, press);
            held.localRotation = Quaternion.Slerp(Quaternion.Euler(AmmoHeldEuler), Quaternion.Euler(AmmoUseEuler), blend);
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
                    if (kind != FarmingItemKind.AmmoPack) Fit(view.firstPerson, FirstPersonMaxSize); // The pack is already hand-sized and held by its handle.
                    else view.firstPerson.transform.localScale = Vector3.one;
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
