using UnityEngine;
using UnityEngine.SceneManagement;
namespace VoidLast.Player
{
    public static class LootSceneSession
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() { SceneManager.sceneLoaded-=Loaded;SceneManager.sceneLoaded+=Loaded; }
        static void Loaded(Scene scene,LoadSceneMode mode)
        {
            if(Stage2NetworkBootstrap.IsOnline)return;
            int seed=Random.Range(1,int.MaxValue);
            foreach(var root in scene.GetRootGameObjects())foreach(var box in root.GetComponentsInChildren<LootContainer>(true))box.SetSeed(seed);
        }
    }
}
