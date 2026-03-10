using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Yuukei.Runtime
{
    /// <summary>
    /// 初回起動判定とスターターパッケージの導入状態補正を担当するブートストラップ。
    /// </summary>
    public sealed class TutorialBootstrap
    {
        private readonly PersistenceStore _persistenceStore;
        private readonly StarterPackageSeeder _starterPackageSeeder;

        public TutorialBootstrap(PersistenceStore persistenceStore, StarterPackageSeeder starterPackageSeeder)
        {
            _persistenceStore = persistenceStore;
            _starterPackageSeeder = starterPackageSeeder;
        }

        /// <summary>セーブファイルの存在で初回起動かどうかを判定する。</summary>
        public bool IsFirstLaunch()
        {
            var result = !File.Exists(_persistenceStore.SaveFilePath);
            Debug.Log("[TutorialBootstrap] IsFirstLaunch: 初回起動判定=" + result);
            return result;
        }

        /// <summary>初回起動判定を返しつつ、スターターパッケージが未導入ならシードする。</summary>
        public async UniTask<bool> EnsureFirstLaunchPackageStateAsync(CancellationToken cancellationToken)
        {
            var isFirstLaunch = IsFirstLaunch();
            if (isFirstLaunch)
            {
                Debug.Log("[TutorialBootstrap] EnsureFirstLaunchPackageStateAsync: 初回起動です。スターターパッケージを確認します");
            }

            var seeded = await _starterPackageSeeder.EnsureInstalledAsync(cancellationToken);
            if (seeded)
            {
                Debug.Log("[TutorialBootstrap] EnsureFirstLaunchPackageStateAsync: スターターパッケージを StreamingAssets から導入しました");
            }

            return isFirstLaunch;
        }
    }
}
