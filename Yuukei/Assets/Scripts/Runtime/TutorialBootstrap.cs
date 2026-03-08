using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Yuukei.Runtime
{
    public sealed class TutorialBootstrap
    {
        private readonly PersistenceStore _persistenceStore;
        private readonly PackageManager _packageManager;

        public TutorialBootstrap(PersistenceStore persistenceStore, PackageManager packageManager)
        {
            _persistenceStore = persistenceStore;
            _packageManager = packageManager;
        }

        public bool IsFirstLaunch()
        {
            return !File.Exists(_persistenceStore.SaveFilePath);
        }

        public async UniTask<bool> EnsureFirstLaunchPackageStateAsync(CancellationToken cancellationToken)
        {
            var isFirstLaunch = IsFirstLaunch();
            if (isFirstLaunch)
            {
                await _packageManager.EnsureStarterPackageAsync(cancellationToken);
            }

            return isFirstLaunch;
        }
    }
}
