using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Yuukei.Runtime
{
    /// <summary>
    /// 初回起動時のチュートリアル処理を担当するブートストラップ。
    /// セーブファイルの有無で初回起動を判定し、スターターパッケージを準備する。
    /// </summary>
    public sealed class TutorialBootstrap
    {
        private readonly PersistenceStore _persistenceStore;
        private readonly PackageManager _packageManager;

        public TutorialBootstrap(PersistenceStore persistenceStore, PackageManager packageManager)
        {
            _persistenceStore = persistenceStore;
            _packageManager = packageManager;
        }

        /// <summary>セーブファイルの存在で初回起動かどうかを判定する。</summary>
        public bool IsFirstLaunch()
        {
            var result = !File.Exists(_persistenceStore.SaveFilePath);
            Debug.Log("[TutorialBootstrap] IsFirstLaunch: 初回起動判定=" + result);
            return result;
        }

        /// <summary>初回起動時にスターターパッケージの準備を行う。</summary>
        public async UniTask<bool> EnsureFirstLaunchPackageStateAsync(CancellationToken cancellationToken)
        {
            var isFirstLaunch = IsFirstLaunch();
            if (isFirstLaunch)
            {
                Debug.Log("[TutorialBootstrap] EnsureFirstLaunchPackageStateAsync: 初回起動のためスターターパッケージを準備します");
                await _packageManager.EnsureStarterPackageAsync(cancellationToken);
                Debug.Log("[TutorialBootstrap] EnsureFirstLaunchPackageStateAsync: スターターパッケージの準備が完了しました");
            }

            return isFirstLaunch;
        }
    }
}
