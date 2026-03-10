using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yuukei.Runtime
{
    /// <summary>
    /// アプリケーション起動時に ResidentAppController の存在を保証するブートストラップ。
    /// シーン読み込み後に自動実行され、コントローラーが無ければ生成する。
    /// </summary>
    public static class YuukeiBootstrap
    {
        /// <summary>シーン読み込み後にランタイムコントローラーの存在を確認・生成する。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeController()
        {
            Debug.Log("[YuukeiBootstrap] EnsureRuntimeController: ブートストラップを実行しています");
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.name != "SampleScene")
            {
                Debug.Log("[YuukeiBootstrap] EnsureRuntimeController: 対象シーンではないためスキップします scene=" + (scene.IsValid() ? scene.name : "(invalid)"));
                return;
            }

            if (Object.FindFirstObjectByType<ResidentAppController>() != null)
            {
                Debug.Log("[YuukeiBootstrap] EnsureRuntimeController: 既存のコントローラーを検出しました。生成をスキップします");
                return;
            }

            Debug.Log("[YuukeiBootstrap] EnsureRuntimeController: コントローラーが見つからないため新規作成します");
            var runtimeObject = new GameObject("YuukeiRuntime");
            runtimeObject.AddComponent<ResidentAppController>();
        }
    }
}
