using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Yuukei.Runtime
{
    /// <summary>組み込みスターターパッケージの識別情報をまとめた定義。</summary>
    internal static class StarterPackageMetadata
    {
        public const string PackageId = "0f479418-2a7a-4c1d-93d8-b6cf7af6bfc0";
        public const string Creator = "yuukei";
        public const string Version = "v0.0.1";

        public static string InstallDirectoryName => $"{Creator}-{Version}-{PackageId}";
    }

    /// <summary>読み取り専用のスターターパッケージ供給元。</summary>
    internal interface IStarterPackageSource
    {
        string Description { get; }

        bool Exists();

        UniTask CopyToAsync(string destinationDirectory, CancellationToken cancellationToken);
    }

    /// <summary>
    /// ファイルシステム上の StreamingAssets からスターターパッケージを供給する。
    /// Windows MVP では通常のディレクトリアクセスで扱える実装を使う。
    /// </summary>
    internal sealed class FileSystemStarterPackageSource : IStarterPackageSource
    {
        private readonly string _sourceDirectory;

        public FileSystemStarterPackageSource(string sourceDirectory = null)
        {
            _sourceDirectory = sourceDirectory ?? GetDefaultSourceDirectory();
        }

        public string Description => _sourceDirectory;

        public bool Exists()
        {
            return !string.IsNullOrWhiteSpace(_sourceDirectory)
                && Directory.Exists(_sourceDirectory)
                && File.Exists(Path.Combine(_sourceDirectory, "manifest.json"));
        }

        public UniTask CopyToAsync(string destinationDirectory, CancellationToken cancellationToken)
        {
            if (!Exists())
            {
                throw new DirectoryNotFoundException($"Starter package source directory was not found: {_sourceDirectory}");
            }

            return PackageDirectoryUtility.CopyDirectoryAsync(_sourceDirectory, destinationDirectory, cancellationToken);
        }

        internal static string GetDefaultSourceDirectory()
        {
            return Path.Combine(Application.streamingAssetsPath, "StarterPackages", StarterPackageMetadata.InstallDirectoryName);
        }
    }

    /// <summary>
    /// StreamingAssets に同梱したスターターパッケージを persistentDataPath/package 配下へシードする。
    /// 読み取り専用ソースの取得方式を差し替えやすいよう、供給元を抽象化している。
    /// </summary>
    public sealed class StarterPackageSeeder
    {
        private readonly string _packageRootDirectory;
        private readonly IStarterPackageSource _starterPackageSource;

        public StarterPackageSeeder(string packageRootDirectory = null)
            : this(packageRootDirectory, null)
        {
        }

        internal StarterPackageSeeder(
            string packageRootDirectory,
            IStarterPackageSource starterPackageSource)
        {
            _packageRootDirectory = packageRootDirectory ?? Path.Combine(Application.persistentDataPath, "package");
            _starterPackageSource = starterPackageSource ?? new FileSystemStarterPackageSource();
        }

        public string StarterInstallDirectory => Path.Combine(_packageRootDirectory, StarterPackageMetadata.InstallDirectoryName);

        public bool IsInstalled()
        {
            var manifestPath = Path.Combine(StarterInstallDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            try
            {
                var manifest = JsonConvert.DeserializeObject<PackageManifest>(File.ReadAllText(manifestPath)) ?? new PackageManifest();
                manifest.Normalize();
                return string.Equals(manifest.Id, StarterPackageMetadata.PackageId, StringComparison.Ordinal);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[StarterPackageSeeder] Failed to inspect starter package manifest '{manifestPath}'. {exception.Message}");
                return false;
            }
        }

        /// <summary>
        /// スターターパッケージが未導入なら読み取り専用ソースからコピーする。
        /// 既に導入済みの場合は上書きしない。
        /// </summary>
        public async UniTask<bool> EnsureInstalledAsync(CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(_packageRootDirectory);

            if (IsInstalled())
            {
                Debug.Log("[StarterPackageSeeder] スターターパッケージは既に導入されています");
                return false;
            }

            if (!_starterPackageSource.Exists())
            {
                Debug.LogWarning($"[StarterPackageSeeder] Starter package source was not found: {_starterPackageSource.Description}");
                return false;
            }

            if (Directory.Exists(StarterInstallDirectory))
            {
                Directory.Delete(StarterInstallDirectory, true);
            }

            await _starterPackageSource.CopyToAsync(StarterInstallDirectory, cancellationToken);
            Debug.Log($"[StarterPackageSeeder] スターターパッケージを導入しました: {StarterInstallDirectory}");
            return true;
        }
    }

    /// <summary>パッケージディレクトリの再帰コピーをまとめたユーティリティ。</summary>
    internal static class PackageDirectoryUtility
    {
        public static async UniTask CopyDirectoryAsync(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            await UniTask.SwitchToThreadPool();
            try
            {
                CopyDirectory(sourceDirectory, destinationDirectory, cancellationToken);
            }
            finally
            {
                await UniTask.SwitchToMainThread(cancellationToken);
            }
        }

        public static void CopyDirectory(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException($"Source directory was not found: {sourceDirectory}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destinationDirectory);

            foreach (var file in Directory.GetFiles(sourceDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var targetFile = Path.Combine(destinationDirectory, Path.GetFileName(file));
                File.Copy(file, targetFile, true);
            }

            foreach (var directory in Directory.GetDirectories(sourceDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var targetDirectory = Path.Combine(destinationDirectory, Path.GetFileName(directory));
                CopyDirectory(directory, targetDirectory, cancellationToken);
            }
        }
    }
}
