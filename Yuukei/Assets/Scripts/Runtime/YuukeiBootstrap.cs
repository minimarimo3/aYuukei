using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yuukei.Runtime
{
    public static class YuukeiBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeController()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.name != "SampleScene")
            {
                return;
            }

            if (Object.FindFirstObjectByType<ResidentAppController>() != null)
            {
                return;
            }

            var runtimeObject = new GameObject("YuukeiRuntime");
            runtimeObject.AddComponent<ResidentAppController>();
        }
    }
}
