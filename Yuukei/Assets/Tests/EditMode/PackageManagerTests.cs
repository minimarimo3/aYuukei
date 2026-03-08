using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Yuukei.Runtime;

namespace Yuukei.Tests.EditMode
{
    public sealed class PackageManagerTests
    {
        [Test]
        public async Task ResolveActiveContent_UsesManifestOrderAndWholeArrayOverride()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "yuukei-package-" + Guid.NewGuid().ToString("N"));
            var packageRoot = Path.Combine(tempDirectory, "package");
            var installRoot = Path.Combine(packageRoot, "creator-v1-guid");
            var overrideRoot = Path.Combine(tempDirectory, "override");
            Directory.CreateDirectory(Path.Combine(installRoot, "Scripts"));
            Directory.CreateDirectory(overrideRoot);

            var firstScriptPath = Path.Combine(installRoot, "Scripts", "first.daihon");
            var secondScriptPath = Path.Combine(installRoot, "Scripts", "second.daihon");
            var overrideFirstPath = Path.Combine(overrideRoot, "override-first.daihon");
            var overrideSecondPath = Path.Combine(overrideRoot, "override-second.daihon");
            var characterPath = Path.Combine(installRoot, "character.vrm");

            File.WriteAllText(firstScriptPath, "## First\n### Scene\n合図: ＠app_started\n「first」");
            File.WriteAllText(secondScriptPath, "## Second\n### Scene\n合図: ＠app_started\n「second」");
            File.WriteAllText(overrideFirstPath, "## OverrideFirst\n### Scene\n合図: ＠app_started\n「override-first」");
            File.WriteAllText(overrideSecondPath, "## OverrideSecond\n### Scene\n合図: ＠app_started\n「override-second」");
            File.WriteAllText(characterPath, string.Empty);

            File.WriteAllText(
                Path.Combine(installRoot, "manifest.json"),
                BuildManifestJson("creator", "v1", "guid", "character.vrm", "Scripts/first.daihon", "Scripts/second.daihon"));

            try
            {
                var store = new PersistenceStore(Path.Combine(tempDirectory, "save.json"));
                var packageManager = new PackageManager(store, packageRootDirectory: packageRoot);
                await packageManager.ReloadInstalledPackagesAsync();
                await packageManager.SwitchActivePackageAsync("guid");

                var manifestSelection = packageManager.GetResolvedActiveContent();
                Assert.That(manifestSelection.DaihonPaths, Is.EqualTo(new[]
                {
                    Path.GetFullPath(firstScriptPath),
                    Path.GetFullPath(secondScriptPath),
                }));

                store.SetOverrides(new OverrideSelections
                {
                    Daihon = new List<string> { overrideFirstPath, overrideSecondPath },
                });

                var overrideSelection = packageManager.GetResolvedActiveContent();
                Assert.That(overrideSelection.DaihonPaths, Is.EqualTo(new[]
                {
                    Path.GetFullPath(overrideFirstPath),
                    Path.GetFullPath(overrideSecondPath),
                }));

                store.SetOverrides(new OverrideSelections());
                var resetSelection = packageManager.GetResolvedActiveContent();
                Assert.That(resetSelection.DaihonPaths, Is.EqualTo(new[]
                {
                    Path.GetFullPath(firstScriptPath),
                    Path.GetFullPath(secondScriptPath),
                }));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public async Task ValidateActivePackage_ReportsMissingDaihonsIndividually()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "yuukei-package-" + Guid.NewGuid().ToString("N"));
            var packageRoot = Path.Combine(tempDirectory, "package");
            var installRoot = Path.Combine(packageRoot, "creator-v1-guid");
            Directory.CreateDirectory(Path.Combine(installRoot, "Scripts"));

            File.WriteAllText(Path.Combine(installRoot, "Scripts", "main.daihon"), "## Main\n### Scene\n合図: ＠app_started\n「hello」");
            File.WriteAllText(Path.Combine(installRoot, "character.vrm"), string.Empty);

            File.WriteAllText(
                Path.Combine(installRoot, "manifest.json"),
                BuildManifestJson(
                    "creator",
                    "v1",
                    "guid",
                    "character.vrm",
                    "Scripts/main.daihon",
                    "Scripts/missing-one.daihon",
                    "Scripts/missing-two.daihon"));

            try
            {
                var store = new PersistenceStore(Path.Combine(tempDirectory, "save.json"));
                var packageManager = new PackageManager(store, packageRootDirectory: packageRoot);
                await packageManager.ReloadInstalledPackagesAsync();
                await packageManager.SwitchActivePackageAsync("guid");

                var report = packageManager.ValidateActivePackage();

                Assert.That(report.Warnings.Count, Is.EqualTo(2));
                Assert.That(report.Warnings[0], Does.Contain("Missing Daihon script"));
                Assert.That(report.Warnings[1], Does.Contain("Missing Daihon script"));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public async Task EnsureStarterPackageAsync_UsesProvidedExampleSource()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "yuukei-starter-" + Guid.NewGuid().ToString("N"));
            var packageRoot = Path.Combine(tempDirectory, "package");
            var exampleRoot = Path.Combine(tempDirectory, "examples", "yuukei_default_package");
            Directory.CreateDirectory(Path.Combine(exampleRoot, "daihon"));
            File.WriteAllText(Path.Combine(exampleRoot, "character.vrm"), string.Empty);
            File.WriteAllText(Path.Combine(exampleRoot, "daihon", "main.daihon"), "## Main\n### Scene\n合図: ＠app_started\n「starter」");
            File.WriteAllText(
                Path.Combine(exampleRoot, "manifest.json"),
                BuildManifestJson("yuukei", "v0.0.1", "0f479418-2a7a-4c1d-93d8-b6cf7af6bfc0", "character.vrm", "daihon/main.daihon"));

            try
            {
                var store = new PersistenceStore(Path.Combine(tempDirectory, "save.json"));
                var packageManager = new PackageManager(
                    store,
                    packageRootDirectory: packageRoot,
                    starterPackageSourceDirectory: exampleRoot);

                await packageManager.EnsureStarterPackageAsync();
                await packageManager.ReloadInstalledPackagesAsync();

                Assert.That(packageManager.InstalledPackages.Count, Is.EqualTo(1));
                Assert.That(packageManager.InstalledPackages[0].PackageId, Is.EqualTo("0f479418-2a7a-4c1d-93d8-b6cf7af6bfc0"));
                Assert.That(File.Exists(Path.Combine(packageManager.InstalledPackages[0].RootDirectory, "manifest.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(packageManager.InstalledPackages[0].RootDirectory, "daihon", "main.daihon")), Is.True);
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public async Task SwitchActivePackageAsync_ResetsOverrides()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "yuukei-switch-" + Guid.NewGuid().ToString("N"));
            var packageRoot = Path.Combine(tempDirectory, "package");
            var firstInstallRoot = Path.Combine(packageRoot, "creator-v1-first");
            var secondInstallRoot = Path.Combine(packageRoot, "creator-v1-second");
            Directory.CreateDirectory(firstInstallRoot);
            Directory.CreateDirectory(secondInstallRoot);

            File.WriteAllText(Path.Combine(firstInstallRoot, "manifest.json"), BuildManifestJson("creator", "v1", "first", "character.vrm"));
            File.WriteAllText(Path.Combine(secondInstallRoot, "manifest.json"), BuildManifestJson("creator", "v1", "second", "character.vrm"));

            try
            {
                var store = new PersistenceStore(Path.Combine(tempDirectory, "save.json"));
                var packageManager = new PackageManager(store, packageRootDirectory: packageRoot);
                await packageManager.ReloadInstalledPackagesAsync();
                await packageManager.SwitchActivePackageAsync("first");

                store.SetOverrides(new OverrideSelections
                {
                    Character = "override.vrm",
                    Daihon = new List<string> { "override.daihon" },
                });

                await packageManager.SwitchActivePackageAsync("second");

                Assert.That(store.Data.Overrides.Character, Is.Empty);
                Assert.That(store.Data.Overrides.Daihon, Is.Empty);
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        private static string BuildManifestJson(string creator, string version, string id, string character, params string[] daihonPaths)
        {
            var escapedPaths = string.Join(",\n", daihonPaths.Select(path => $"    \"{path}\""));
            return
$@"{{
  ""creator"": ""{creator}"",
  ""version"": ""{version}"",
  ""id"": ""{id}"",
  ""character"": ""{character}"",
  ""daihon"": [
{escapedPaths}
  ]
}}";
        }
    }

    public sealed class DaihonBridgeTests
    {
        [Test]
        public async Task RaiseEventAsync_DispatchesScriptsInOrder_AndResetsTransientStateBetweenScripts()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "yuukei-daihon-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var firstScriptPath = Path.Combine(tempDirectory, "first.daihon");
            var secondScriptPath = Path.Combine(tempDirectory, "second.daihon");
            File.WriteAllText(firstScriptPath,
@"## First
### Scene
合図: ＠app_started
＜show_dialog 「waiting」＞
_temp=「from script1」
_answer=＜show_choices 「continue」＞
＜show_dialog 「after-wait」＞");
            File.WriteAllText(secondScriptPath,
@"## Second
初期値:
_temp=「from defaults」

### Scene
合図: ＠app_started
＜show_dialog _event_character_id + 「|」 + _temp＞");

            var root = new GameObject("DaihonBridgeTests");

            try
            {
                var persistenceStore = new PersistenceStore(Path.Combine(tempDirectory, "save.json"));
                var aliasRegistry = new AliasRegistry();
                var variableStore = new YuukeiVariableStore(persistenceStore);
                var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasObject.transform.SetParent(root.transform, false);
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var cameraObject = new GameObject("Camera", typeof(Camera));
                cameraObject.transform.SetParent(root.transform, false);
                var camera = cameraObject.GetComponent<Camera>();
                var speechBubbleController = root.AddComponent<SpeechBubbleController>();
                var choiceOverlayController = root.AddComponent<ChoiceOverlayController>();
                var mascotRuntime = root.AddComponent<MascotRuntime>();
                var bridge = new DaihonBridge(aliasRegistry, variableStore, speechBubbleController, choiceOverlayController, mascotRuntime);
                speechBubbleController.Initialize(canvas, camera, () => Vector3.zero);
                choiceOverlayController.Initialize(canvas);
                await bridge.ApplyActivePackageAsync(
                    new PackageContentSelection
                    {
                        DaihonPaths = new List<string> { firstScriptPath, secondScriptPath },
                    },
                    new PackageAliasManifest(),
                    CancellationToken.None);

                var task = bridge.RaiseEventAsync(
                    "app_started",
                    new Dictionary<string, object>
                    {
                        ["_event_character_id"] = "mascot-01",
                    },
                    CancellationToken.None).AsTask();

                Assert.That(SpinWait.SpinUntil(() => IsChoiceOverlayVisible(choiceOverlayController), TimeSpan.FromSeconds(1)), Is.True);
                Assert.That(GetSpeechText(speechBubbleController), Is.EqualTo("waiting"));

                choiceOverlayController.CancelCurrent();
                await task;

                Assert.That(GetSpeechText(speechBubbleController), Is.EqualTo("mascot-01|from defaults"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                Directory.Delete(tempDirectory, true);
            }
        }

        private static bool IsChoiceOverlayVisible(ChoiceOverlayController controller)
        {
            var panelField = typeof(ChoiceOverlayController).GetField("_panelRoot", BindingFlags.Instance | BindingFlags.NonPublic);
            var panel = panelField?.GetValue(controller) as RectTransform;
            return panel != null && panel.gameObject.activeSelf;
        }

        private static string GetSpeechText(SpeechBubbleController controller)
        {
            var labelField = typeof(SpeechBubbleController).GetField("_label", BindingFlags.Instance | BindingFlags.NonPublic);
            var label = labelField?.GetValue(controller) as Text;
            return label?.text ?? string.Empty;
        }
    }
}
