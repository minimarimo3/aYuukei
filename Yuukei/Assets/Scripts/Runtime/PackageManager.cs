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
    public sealed class PackageManager : IPackageContentResolver
    {
        private const string StarterPackageId = "0f479418-2a7a-4c1d-93d8-b6cf7af6bfc0";
        private const string StarterCreator = "yuukei";
        private const string StarterVersion = "v0.0.1";

        private readonly PersistenceStore _persistenceStore;
        private readonly IPackageContentResolver _contentResolver;
        private readonly string _packageRootDirectory;
        private readonly List<ResolvedPackage> _installedPackages = new List<ResolvedPackage>();

        public PackageManager(PersistenceStore persistenceStore, IPackageContentResolver contentResolver = null, string packageRootDirectory = null)
        {
            _persistenceStore = persistenceStore;
            _contentResolver = contentResolver ?? this;
            _packageRootDirectory = packageRootDirectory ?? Path.Combine(Application.persistentDataPath, "package");
        }

        public event Action<ResolvedPackage> ActivePackageChanged;
        public event Action<IReadOnlyList<ResolvedPackage>> InstalledPackagesChanged;

        public IReadOnlyList<ResolvedPackage> InstalledPackages => _installedPackages;
        public ResolvedPackage ActivePackage { get; private set; }

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(_packageRootDirectory);
            await EnsureStarterPackageAsync(cancellationToken);
            await ReloadInstalledPackagesAsync(cancellationToken);

            var desiredId = _persistenceStore.Data.ActivePackageId;
            var resolved = _installedPackages.FirstOrDefault(package => package.PackageId == desiredId)
                ?? _installedPackages.FirstOrDefault(package => package.PackageId == StarterPackageId)
                ?? _installedPackages.FirstOrDefault();

            if (resolved != null)
            {
                await SwitchActivePackageAsync(resolved.PackageId, cancellationToken);
            }
        }

        public async UniTask EnsureStarterPackageAsync(CancellationToken cancellationToken = default)
        {
            var starterDirectory = GetInstallDirectory(StarterCreator, StarterVersion, StarterPackageId);
            if (Directory.Exists(starterDirectory) && File.Exists(Path.Combine(starterDirectory, "manifest.json")))
            {
                return;
            }

            Directory.CreateDirectory(starterDirectory);
            Directory.CreateDirectory(Path.Combine(starterDirectory, "Scripts"));
            Directory.CreateDirectory(Path.Combine(starterDirectory, "Textures"));

            var manifest = new PackageManifest
            {
                Creator = StarterCreator,
                Version = StarterVersion,
                Id = StarterPackageId,
                License = "Placeholder starter package",
                Daihon = new List<string> { "Scripts/main.daihon" },
                Character = "character.vrm",
                Textures = new Dictionary<string, PackageTextureManifest>
                {
                    ["speechBubble"] = new PackageTextureManifest
                    {
                        Background = "Textures/speech_bubble_bg.png",
                        Tail = "Textures/speech_bubble_tail.png",
                    }
                }
            };

            var manifestJson = JsonConvert.SerializeObject(manifest, Formatting.Indented);
            await File.WriteAllTextAsync(Path.Combine(starterDirectory, "manifest.json"), manifestJson, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(starterDirectory, "Scripts", "main.daihon"), BuildStarterScript(), cancellationToken);

            await WriteSolidTextureAsync(Path.Combine(starterDirectory, "Textures", "speech_bubble_bg.png"), new Color32(30, 38, 55, 245), cancellationToken);
            await WriteSolidTextureAsync(Path.Combine(starterDirectory, "Textures", "speech_bubble_tail.png"), new Color32(30, 38, 55, 245), cancellationToken);
        }

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

            InstalledPackagesChanged?.Invoke(_installedPackages);
        }

        public async UniTask SwitchActivePackageAsync(string packageId, CancellationToken cancellationToken = default)
        {
            var package = _installedPackages.FirstOrDefault(entry => entry.PackageId == packageId);
            if (package == null)
            {
                throw new InvalidOperationException($"Package '{packageId}' is not installed.");
            }

            ActivePackage = package;
            _persistenceStore.SetActivePackageId(package.PackageId);
            _persistenceStore.ResetOverrides();
            await _persistenceStore.SaveAsync(cancellationToken);
            ActivePackageChanged?.Invoke(package);
        }

        public async UniTask<bool> ImportPackageFromFolderAsync(string folderPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                return false;
            }

            var manifestPath = Path.Combine(folderPath, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            var manifest = JsonConvert.DeserializeObject<PackageManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken));
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id))
            {
                return false;
            }

            manifest.Normalize();
            var destination = GetInstallDirectory(manifest.Creator, manifest.Version, manifest.Id);
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, true);
            }

            CopyDirectory(folderPath, destination);
            await ReloadInstalledPackagesAsync(cancellationToken);
            return true;
        }

        public async UniTask DeletePackageAsync(string packageId, CancellationToken cancellationToken = default)
        {
            var package = _installedPackages.FirstOrDefault(entry => entry.PackageId == packageId);
            if (package == null)
            {
                return;
            }

            Directory.Delete(package.RootDirectory, true);
            await ReloadInstalledPackagesAsync(cancellationToken);

            if (ActivePackage?.PackageId == packageId)
            {
                var fallback = _installedPackages.FirstOrDefault(entry => entry.PackageId == StarterPackageId) ?? _installedPackages.FirstOrDefault();
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

        public PackageValidationReport ValidateActivePackage()
        {
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

        private static string BuildStarterScript()
        {
            return
@"## YuukeiStarter
初期値:
    チュートリアル済み=いいえ

### 起動時の挨拶
合図: ＠起動時
「こんにちは。デスクトップで一緒に過ごそう。」
＜show_dialog 「設定と終了はショートカットかメニューから開けます。」＞

### クリック反応
合図: ＠クリック
「呼んだ？」

### ダブルクリック反応
合図: ＠ダブルクリック
_返答=＜show_choices 「よろしく」 「あとで」＞
※（_返答=「よろしく」）:
    「よろしくね。」
おわり

### 放置
合図: ＠放置
「作業中かな。邪魔しないようにするね。」

### 定期発火
合図: ＠定期発火
「ここにいるよ。」

### 既定
「今日もよろしくね。」
";
        }

        private static async UniTask WriteSolidTextureAsync(string path, Color32 color, CancellationToken cancellationToken)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color32[16];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            var bytes = texture.EncodeToPNG();
            UnityEngine.Object.Destroy(texture);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (var file in Directory.GetFiles(sourceDirectory))
            {
                var targetFile = Path.Combine(destinationDirectory, Path.GetFileName(file));
                File.Copy(file, targetFile, true);
            }

            foreach (var directory in Directory.GetDirectories(sourceDirectory))
            {
                var targetDirectory = Path.Combine(destinationDirectory, Path.GetFileName(directory));
                CopyDirectory(directory, targetDirectory);
            }
        }
    }
}
