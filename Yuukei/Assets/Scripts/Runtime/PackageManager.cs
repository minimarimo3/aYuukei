using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Yuukei.Runtime
{
    /// <summary>
    /// パッケージの検出・切り替え・インポート・削除を管理するクラス。
    /// 導入済みパッケージ一覧の再構築と、永続化されたアクティブパッケージの復元を担当する。
    /// </summary>
    public sealed class PackageManager : IPackageContentResolver
    {
        private readonly PersistenceStore _persistenceStore;
        private readonly IPackageContentResolver _contentResolver;
        private readonly string _packageRootDirectory;
        private readonly List<ResolvedPackage> _installedPackages = new List<ResolvedPackage>();

        public PackageManager(
            PersistenceStore persistenceStore,
            IPackageContentResolver contentResolver = null,
            string packageRootDirectory = null)
        {
            _persistenceStore = persistenceStore;
            _contentResolver = contentResolver ?? this;
            _packageRootDirectory = packageRootDirectory ?? Path.Combine(Application.persistentDataPath, "package");
        }

        public event Action<ResolvedPackage> ActivePackageChanged;
        public event Action<IReadOnlyList<ResolvedPackage>> InstalledPackagesChanged;

        public IReadOnlyList<ResolvedPackage> InstalledPackages => _installedPackages;
        public ResolvedPackage ActivePackage { get; private set; }

        /// <summary>初期化: パッケージ一覧読み込み → アクティブ復元。</summary>
        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            Debug.Log("[PackageManager] 初期化を開始します");
            Directory.CreateDirectory(_packageRootDirectory);
            await ReloadInstalledPackagesAsync(cancellationToken);

            var desiredId = _persistenceStore.Data.ActivePackageId;
            var resolved = _installedPackages.FirstOrDefault(package => package.PackageId == desiredId)
                ?? _installedPackages.FirstOrDefault(package => package.PackageId == StarterPackageMetadata.PackageId)
                ?? _installedPackages.FirstOrDefault();

            Debug.Log($"[PackageManager] 検出パッケージ数={_installedPackages.Count}, 希望ID={desiredId}, 解決={resolved?.PackageId ?? "(なし)"}");

            if (resolved != null)
            {
                await SwitchActivePackageAsync(resolved.PackageId, cancellationToken);
            }

            Debug.Log("[PackageManager] 初期化が完了しました");
        }

        /// <summary>パッケージルートを再スキャンし、一覧を更新する。</summary>
        public async UniTask ReloadInstalledPackagesAsync(CancellationToken cancellationToken = default)
        {
            _installedPackages.Clear();
            Directory.CreateDirectory(_packageRootDirectory);

            foreach (var directory in Directory.GetDirectories(_packageRootDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var manifestPath = Path.Combine(directory, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                try
                {
                    var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                    var manifest = JsonConvert.DeserializeObject<PackageManifest>(json) ?? new PackageManifest();
                    manifest.Normalize();
                    if (string.IsNullOrWhiteSpace(manifest.Id))
                    {
                        Debug.LogWarning($"[PackageManager] Skipping package in '{directory}' because manifest.json has no id.");
                        continue;
                    }

                    _installedPackages.Add(new ResolvedPackage(directory, manifest));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[PackageManager] Failed to read package manifest '{manifestPath}': {exception.Message}");
                }
            }

            Debug.Log($"[PackageManager] パッケージ再スキャン完了: {_installedPackages.Count}件検出");
            InstalledPackagesChanged?.Invoke(_installedPackages);
        }

        /// <summary>アクティブパッケージを切り替える。</summary>
        public async UniTask SwitchActivePackageAsync(string packageId, CancellationToken cancellationToken = default)
        {
            Debug.Log($"[PackageManager] パッケージ切り替え開始: {packageId}");
            var package = _installedPackages.FirstOrDefault(entry => entry.PackageId == packageId);
            if (package == null)
            {
                throw new InvalidOperationException($"Package '{packageId}' is not installed.");
            }

            ActivePackage = package;
            _persistenceStore.SetActivePackageId(package.PackageId);
            _persistenceStore.ResetOverrides();
            _persistenceStore.RequestSave();
            ActivePackageChanged?.Invoke(package);
            Debug.Log($"[PackageManager] パッケージ切り替え完了: {package.PackageId}");
            await UniTask.CompletedTask;
        }

        /// <summary>フォルダからパッケージをインポートする。</summary>
        public async UniTask<bool> ImportPackageFromFolderAsync(string folderPath, CancellationToken cancellationToken = default)
        {
            Debug.Log($"[PackageManager] フォルダからインポート開始: {folderPath}");
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                Debug.LogWarning($"[PackageManager] インポート失敗: フォルダが存在しません ({folderPath})");
                return false;
            }

            var manifestPath = Path.Combine(folderPath, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning("[PackageManager] インポート失敗: manifest.json が見つかりません");
                return false;
            }

            var manifest = JsonConvert.DeserializeObject<PackageManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken));
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id))
            {
                Debug.LogWarning("[PackageManager] インポート失敗: マニフェストが無効です");
                return false;
            }

            manifest.Normalize();
            var destination = GetInstallDirectory(manifest.Creator, manifest.Version, manifest.Id);
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, true);
            }

            await PackageDirectoryUtility.CopyDirectoryAsync(folderPath, destination, cancellationToken);
            await ReloadInstalledPackagesAsync(cancellationToken);
            Debug.Log($"[PackageManager] インポート成功: {manifest.Id}");
            return true;
        }

        /// <summary>指定パッケージを削除する。アクティブだった場合はフォールバックに切り替える。</summary>
        public async UniTask DeletePackageAsync(string packageId, CancellationToken cancellationToken = default)
        {
            Debug.Log($"[PackageManager] パッケージ削除開始: {packageId}");
            var package = _installedPackages.FirstOrDefault(entry => entry.PackageId == packageId);
            if (package == null)
            {
                Debug.Log($"[PackageManager] 削除対象のパッケージが見つかりません: {packageId}");
                return;
            }

            Directory.Delete(package.RootDirectory, true);
            await ReloadInstalledPackagesAsync(cancellationToken);
            Debug.Log($"[PackageManager] パッケージを削除しました: {packageId}");

            if (ActivePackage?.PackageId == packageId)
            {
                var fallback = _installedPackages.FirstOrDefault(entry => entry.PackageId == StarterPackageMetadata.PackageId)
                    ?? _installedPackages.FirstOrDefault();
                if (fallback != null)
                {
                    await SwitchActivePackageAsync(fallback.PackageId, cancellationToken);
                }
            }
        }

        public PackageContentSelection GetResolvedActiveContent()
        {
            return ActivePackage == null
                ? new PackageContentSelection()
                : _contentResolver.ResolveActiveContent(ActivePackage, _persistenceStore.Data.Overrides);
        }

        public PackageContentSelection ResolveActiveContent(ResolvedPackage package, OverrideSelections overrides)
        {
            var selection = new PackageContentSelection();
            if (package == null)
            {
                return selection;
            }

            selection.DaihonPaths = ResolveOrderedFiles(package, overrides?.Daihon, package.Manifest.Daihon);
            selection.CharacterPath = ResolveFile(package, overrides?.Character, package.Manifest.Character);

            foreach (var pair in package.Manifest.Textures)
            {
                if (!string.IsNullOrWhiteSpace(pair.Value?.Background))
                {
                    selection.TexturePaths[$"{pair.Key}.background"] = ResolveFile(package, GetDictionaryValue(overrides?.Textures, $"{pair.Key}.background"), pair.Value.Background);
                }

                if (!string.IsNullOrWhiteSpace(pair.Value?.Tail))
                {
                    selection.TexturePaths[$"{pair.Key}.tail"] = ResolveFile(package, GetDictionaryValue(overrides?.Textures, $"{pair.Key}.tail"), pair.Value.Tail);
                }
            }

            foreach (var assetPath in package.Manifest.Assets)
            {
                selection.AssetPaths[Path.GetFileNameWithoutExtension(assetPath)] = ResolveFile(package, GetDictionaryValue(overrides?.Assets, assetPath), assetPath);
            }

            foreach (var motionPath in package.Manifest.Motions)
            {
                selection.MotionPaths[Path.GetFileNameWithoutExtension(motionPath)] = ResolveFile(package, GetDictionaryValue(overrides?.Motions, motionPath), motionPath);
            }

            foreach (var dllPath in package.Manifest.Dlls)
            {
                var resolved = ResolveFile(package, null, dllPath);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    selection.DllPaths.Add(resolved);
                }
            }

            return selection;
        }

        /// <summary>アクティブパッケージのファイル整合性を検証する。</summary>
        public PackageValidationReport ValidateActivePackage()
        {
            Debug.Log("[PackageManager] アクティブパッケージの検証を開始します");
            var report = new PackageValidationReport();
            var content = GetResolvedActiveContent();
            foreach (var daihonPath in content.DaihonPaths)
            {
                if (!File.Exists(daihonPath))
                {
                    report.Warnings.Add($"Missing Daihon script: {daihonPath}");
                }
            }

            if (!string.IsNullOrWhiteSpace(content.CharacterPath) && !File.Exists(content.CharacterPath))
            {
                report.Warnings.Add($"Missing character VRM: {content.CharacterPath}");
            }

            foreach (var pair in content.TexturePaths)
            {
                if (!File.Exists(pair.Value))
                {
                    report.Warnings.Add($"Missing texture '{pair.Key}': {pair.Value}");
                }
            }

            foreach (var pair in content.MotionPaths)
            {
                if (!File.Exists(pair.Value))
                {
                    report.Warnings.Add($"Missing motion '{pair.Key}': {pair.Value}");
                }
            }

            foreach (var dllPath in content.DllPaths)
            {
                if (!File.Exists(dllPath))
                {
                    report.Warnings.Add($"Missing DLL: {dllPath}");
                }
            }

            if (report.Warnings.Count > 0)
            {
                Debug.LogWarning($"[PackageManager] 検証完了: {report.Warnings.Count}件の警告があります");
            }
            else
            {
                Debug.Log("[PackageManager] 検証完了: 問題なし");
            }

            return report;
        }

        private string GetInstallDirectory(string creator, string version, string guid)
        {
            return Path.Combine(_packageRootDirectory, $"{creator}-{version}-{guid}");
        }

        private static string GetDictionaryValue(IReadOnlyDictionary<string, string> dictionary, string key)
        {
            if (dictionary == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return dictionary.TryGetValue(key, out var value) ? value : string.Empty;
        }

        private static List<string> ResolveOrderedFiles(ResolvedPackage package, IReadOnlyList<string> overrideValues, IReadOnlyList<string> packageRelativePaths)
        {
            var resolvedPaths = new List<string>();
            if (overrideValues != null && overrideValues.Count > 0)
            {
                foreach (var overrideValue in overrideValues)
                {
                    var resolvedOverride = ResolveOverrideFile(overrideValue);
                    if (!string.IsNullOrWhiteSpace(resolvedOverride))
                    {
                        resolvedPaths.Add(resolvedOverride);
                    }
                }

                return resolvedPaths;
            }

            if (packageRelativePaths == null)
            {
                return resolvedPaths;
            }

            foreach (var packageRelativePath in packageRelativePaths)
            {
                var resolvedPackagePath = ResolvePackageRelativeFile(package, packageRelativePath);
                if (!string.IsNullOrWhiteSpace(resolvedPackagePath))
                {
                    resolvedPaths.Add(resolvedPackagePath);
                }
            }

            return resolvedPaths;
        }

        private static string ResolveFile(ResolvedPackage package, string overrideValue, string packageRelativePath)
        {
            if (!string.IsNullOrWhiteSpace(overrideValue))
            {
                return ResolveOverrideFile(overrideValue);
            }

            return ResolvePackageRelativeFile(package, packageRelativePath);
        }

        private static string ResolveOverrideFile(string overrideValue)
        {
            if (string.IsNullOrWhiteSpace(overrideValue))
            {
                return string.Empty;
            }

            return Path.GetFullPath(overrideValue);
        }

        private static string ResolvePackageRelativeFile(ResolvedPackage package, string packageRelativePath)
        {
            if (package == null || string.IsNullOrWhiteSpace(packageRelativePath) || Path.IsPathRooted(packageRelativePath))
            {
                return string.Empty;
            }

            return package.GetAbsolutePath(packageRelativePath);
        }
    }
}
