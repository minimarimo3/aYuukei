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
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Yuukei.Runtime;

namespace Yuukei.Tests.EditMode
{
    internal static class PackageTestUtility
    {
        public static string BuildManifestJson(string creator, string version, string id, string character, params string[] daihonPaths)
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

    internal static class ChoiceOverlayTestUtility
    {
        public static T GetPrivateField<T>(object instance, string fieldName) where T : class
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(instance) as T;
        }

        public static bool IsChoiceOverlayVisible(ChoiceOverlayController controller)
        {
            var panel = GetPrivateField<RectTransform>(controller, "_panelRoot");
            return panel != null && panel.gameObject.activeSelf;
        }
    }

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
                PackageTestUtility.BuildManifestJson("creator", "v1", "guid", "character.vrm", "Scripts/first.daihon", "Scripts/second.daihon"));

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
                PackageTestUtility.BuildManifestJson(
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
        public async Task InitializeAsync_SelectsStarterPackageWhenActivePackageIdIsEmpty()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "yuukei-starter-init-" + Guid.NewGuid().ToString("N"));
            var packageRoot = Path.Combine(tempDirectory, "package");
            var starterRoot = Path.Combine(packageRoot, StarterPackageMetadata.InstallDirectoryName);
            var otherRoot = Path.Combine(packageRoot, "creator-v9-other");

            Directory.CreateDirectory(Path.Combine(starterRoot, "Scripts"));
            Directory.CreateDirectory(otherRoot);
            File.WriteAllText(Path.Combine(starterRoot, "character.vrm"), string.Empty);
            File.WriteAllText(Path.Combine(starterRoot, "Scripts", "main.daihon"), "## Main\n### Scene\n合図: ＠app_started\n「starter」");
            File.WriteAllText(
                Path.Combine(starterRoot, "manifest.json"),
                PackageTestUtility.BuildManifestJson(StarterPackageMetadata.Creator, StarterPackageMetadata.Version, StarterPackageMetadata.PackageId, "character.vrm", "Scripts/main.daihon"));
            File.WriteAllText(
                Path.Combine(otherRoot, "manifest.json"),
                PackageTestUtility.BuildManifestJson("creator", "v9", "other", "character.vrm"));

            try
            {
                var store = new PersistenceStore(Path.Combine(tempDirectory, "save.json"));
                var packageManager = new PackageManager(store, packageRootDirectory: packageRoot);

                await packageManager.InitializeAsync();

                Assert.That(packageManager.ActivePackage, Is.Not.Null);
                Assert.That(packageManager.ActivePackage.PackageId, Is.EqualTo(StarterPackageMetadata.PackageId));
                Assert.That(store.Data.ActivePackageId, Is.EqualTo(StarterPackageMetadata.PackageId));
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

            File.WriteAllText(Path.Combine(firstInstallRoot, "manifest.json"), PackageTestUtility.BuildManifestJson("creator", "v1", "first", "character.vrm"));
            File.WriteAllText(Path.Combine(secondInstallRoot, "manifest.json"), PackageTestUtility.BuildManifestJson("creator", "v1", "second", "character.vrm"));

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

    }

    public sealed class ChoiceOverlayControllerTests
    {
        [Test]
        public void ShowChoicesAsync_BuildsVisibleButtonsAndReturnsSelection()
        {
            var root = new GameObject("ChoiceOverlayControllerTests");
            var initialEventSystems = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);

            try
            {
                var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasObject.transform.SetParent(root.transform, false);
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                var controller = root.AddComponent<ChoiceOverlayController>();
                controller.Initialize(canvas);

                var task = controller.ShowChoicesAsync(new[] { "A", "B" }, CancellationToken.None).AsTask();
                var panel = ChoiceOverlayTestUtility.GetPrivateField<RectTransform>(controller, "_panelRoot");
                var card = ChoiceOverlayTestUtility.GetPrivateField<RectTransform>(controller, "_cardRoot");
                var buttons = ChoiceOverlayTestUtility.GetPrivateField<RectTransform>(controller, "_buttonContainer");

                Assert.That(panel, Is.Not.Null);
                Assert.That(card, Is.Not.Null);
                Assert.That(buttons, Is.Not.Null);

                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(buttons);
                LayoutRebuilder.ForceRebuildLayoutImmediate(card);
                LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
                Canvas.ForceUpdateCanvases();

                Assert.That(controller.IsShowing, Is.True);
                Assert.That(panel.gameObject.activeSelf, Is.True);
                Assert.That(buttons.childCount, Is.EqualTo(2));
                Assert.That(card.rect.height, Is.GreaterThan(0f));
                Assert.That(buttons.rect.height, Is.GreaterThan(0f));

                Button firstButton = null;
                foreach (Transform child in buttons)
                {
                    var rect = child as RectTransform;
                    var button = child.GetComponent<Button>();
                    Assert.That(rect, Is.Not.Null);
                    Assert.That(button, Is.Not.Null);
                    Assert.That(button.interactable, Is.True);
                    Assert.That(rect.rect.height, Is.GreaterThan(0f));
                    Assert.That(rect.localScale, Is.EqualTo(Vector3.one));
                    Assert.That(button.image.color.a, Is.GreaterThan(0f));
                    firstButton ??= button;
                }

                Assert.That(firstButton, Is.Not.Null);
                Assert.That(UnityEngine.Object.FindFirstObjectByType<EventSystem>(), Is.Not.Null);

                firstButton.onClick.Invoke();

                Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo("A"));
                Assert.That(controller.IsShowing, Is.False);
            }
            finally
            {
                foreach (var eventSystem in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                {
                    var existedBeforeTest = Array.Exists(initialEventSystems, existing => existing == eventSystem);
                    if (!existedBeforeTest)
                    {
                        UnityEngine.Object.DestroyImmediate(eventSystem.gameObject);
                    }
                }

                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }

    public sealed class StarterPackageSeederTests
    {
        [Test]
        public async Task EnsureInstalledAsync_CopiesStarterPackageFromSourceDirectory()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "yuukei-seeder-" + Guid.NewGuid().ToString("N"));
            var packageRoot = Path.Combine(tempDirectory, "package");
            var sourceRoot = Path.Combine(tempDirectory, "streaming", "StarterPackages", StarterPackageMetadata.InstallDirectoryName);
            Directory.CreateDirectory(Path.Combine(sourceRoot, "Scripts"));
            Directory.CreateDirectory(Path.Combine(sourceRoot, "Textures"));
            File.WriteAllText(Path.Combine(sourceRoot, "character.vrm"), string.Empty);
            File.WriteAllText(Path.Combine(sourceRoot, "Scripts", "main.daihon"), "## Main\n### Scene\n合図: ＠app_started\n「starter」");
            File.WriteAllText(Path.Combine(sourceRoot, "Textures", "speech_bubble_bg.png"), "bg");
            File.WriteAllText(Path.Combine(sourceRoot, "Textures", "speech_bubble_tail.png"), "tail");
            File.WriteAllText(
                Path.Combine(sourceRoot, "manifest.json"),
                PackageTestUtility.BuildManifestJson(
                    StarterPackageMetadata.Creator,
                    StarterPackageMetadata.Version,
                    StarterPackageMetadata.PackageId,
                    "character.vrm",
                    "Scripts/main.daihon"));

            try
            {
                var seeder = new StarterPackageSeeder(
                    packageRoot,
                    new FileSystemStarterPackageSource(sourceRoot));

                var copied = await seeder.EnsureInstalledAsync();
                var installRoot = Path.Combine(packageRoot, StarterPackageMetadata.InstallDirectoryName);

                Assert.That(copied, Is.True);
                Assert.That(File.Exists(Path.Combine(installRoot, "manifest.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(installRoot, "Scripts", "main.daihon")), Is.True);
                Assert.That(File.Exists(Path.Combine(installRoot, "Textures", "speech_bubble_bg.png")), Is.True);
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public async Task EnsureInstalledAsync_DoesNotOverwriteExistingStarterPackage()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "yuukei-seeder-skip-" + Guid.NewGuid().ToString("N"));
            var packageRoot = Path.Combine(tempDirectory, "package");
            var sourceRoot = Path.Combine(tempDirectory, "streaming", "StarterPackages", StarterPackageMetadata.InstallDirectoryName);
            Directory.CreateDirectory(Path.Combine(sourceRoot, "Scripts"));
            File.WriteAllText(Path.Combine(sourceRoot, "character.vrm"), string.Empty);
            File.WriteAllText(Path.Combine(sourceRoot, "Scripts", "main.daihon"), "## Main\n### Scene\n合図: ＠app_started\n「starter source」");
            File.WriteAllText(
                Path.Combine(sourceRoot, "manifest.json"),
                PackageTestUtility.BuildManifestJson(
                    StarterPackageMetadata.Creator,
                    StarterPackageMetadata.Version,
                    StarterPackageMetadata.PackageId,
                    "character.vrm",
                    "Scripts/main.daihon"));

            try
            {
                var seeder = new StarterPackageSeeder(
                    packageRoot,
                    new FileSystemStarterPackageSource(sourceRoot));

                await seeder.EnsureInstalledAsync();

                var installedScriptPath = Path.Combine(packageRoot, StarterPackageMetadata.InstallDirectoryName, "Scripts", "main.daihon");
                File.WriteAllText(installedScriptPath, "locally modified");

                var copiedAgain = await seeder.EnsureInstalledAsync();

                Assert.That(copiedAgain, Is.False);
                Assert.That(File.ReadAllText(installedScriptPath), Is.EqualTo("locally modified"));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }

    public sealed class TutorialBootstrapTests
    {
        [Test]
        public async Task EnsureFirstLaunchPackageStateAsync_SeedsStarterPackageEvenWhenSaveAlreadyExists()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "yuukei-bootstrap-" + Guid.NewGuid().ToString("N"));
            var savePath = Path.Combine(tempDirectory, "save.json");
            var packageRoot = Path.Combine(tempDirectory, "package");
            var sourceRoot = Path.Combine(tempDirectory, "streaming", "StarterPackages", StarterPackageMetadata.InstallDirectoryName);
            Directory.CreateDirectory(Path.Combine(sourceRoot, "Scripts"));
            File.WriteAllText(savePath, "{\"activePackageId\":\"\"}");
            File.WriteAllText(Path.Combine(sourceRoot, "character.vrm"), string.Empty);
            File.WriteAllText(Path.Combine(sourceRoot, "Scripts", "main.daihon"), "## Main\n### Scene\n合図: ＠app_started\n「starter」");
            File.WriteAllText(
                Path.Combine(sourceRoot, "manifest.json"),
                PackageTestUtility.BuildManifestJson(
                    StarterPackageMetadata.Creator,
                    StarterPackageMetadata.Version,
                    StarterPackageMetadata.PackageId,
                    "character.vrm",
                    "Scripts/main.daihon"));

            try
            {
                var store = new PersistenceStore(savePath);
                await store.LoadAsync();
                var bootstrap = new TutorialBootstrap(
                    store,
                    new StarterPackageSeeder(packageRoot, new FileSystemStarterPackageSource(sourceRoot)));

                var isFirstLaunch = await bootstrap.EnsureFirstLaunchPackageStateAsync(CancellationToken.None);

                Assert.That(isFirstLaunch, Is.False);
                Assert.That(File.Exists(Path.Combine(packageRoot, StarterPackageMetadata.InstallDirectoryName, "manifest.json")), Is.True);
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }

    public sealed class DaihonBridgeTests
    {
        [Test]
        public async Task RaiseEventAsync_CharacterDoubleClick_ShowsChoicesAndReturnsSelectedLabel()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "yuukei-double-click-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var scriptPath = Path.Combine(tempDirectory, "interaction.daihon");
            File.WriteAllText(scriptPath,
@"## Interactions
### DoubleClick
合図: ＠ダブルクリック
_answer=＜show_choices 「よろしく」 「あとで」＞
＜show_dialog _answer＞");

            var root = new GameObject("DaihonBridgeDoubleClickTests");

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
                        DaihonPaths = new List<string> { scriptPath },
                    },
                    new PackageAliasManifest(),
                    CancellationToken.None);

                var task = bridge.RaiseEventAsync(
                    "character_double_clicked",
                    new Dictionary<string, object>
                    {
                        ["_event_x"] = 120f,
                        ["_event_y"] = 240f,
                        ["_event_character_id"] = "mascot-01",
                    },
                    CancellationToken.None).AsTask();

                Assert.That(SpinWait.SpinUntil(() => ChoiceOverlayTestUtility.IsChoiceOverlayVisible(choiceOverlayController), TimeSpan.FromSeconds(1)), Is.True);

                var card = ChoiceOverlayTestUtility.GetPrivateField<RectTransform>(choiceOverlayController, "_cardRoot");
                var buttons = ChoiceOverlayTestUtility.GetPrivateField<RectTransform>(choiceOverlayController, "_buttonContainer");
                Assert.That(card, Is.Not.Null);
                Assert.That(buttons, Is.Not.Null);
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(buttons);
                LayoutRebuilder.ForceRebuildLayoutImmediate(card);
                Canvas.ForceUpdateCanvases();

                Assert.That(choiceOverlayController.IsShowing, Is.True);
                Assert.That(card.rect.height, Is.GreaterThan(0f));
                Assert.That(buttons.rect.height, Is.GreaterThan(0f));
                Assert.That(buttons.childCount, Is.EqualTo(2));

                var firstButton = buttons.GetChild(0).GetComponent<Button>();
                Assert.That(firstButton, Is.Not.Null);
                Assert.That(firstButton.interactable, Is.True);

                firstButton.onClick.Invoke();
                await task;

                Assert.That(choiceOverlayController.IsShowing, Is.False);
                Assert.That(GetSpeechText(speechBubbleController), Is.EqualTo("よろしく"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                Directory.Delete(tempDirectory, true);
            }
        }

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
_message=_event_character_id + 「|」 + _temp
＜show_dialog _message＞");

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
            return ChoiceOverlayTestUtility.IsChoiceOverlayVisible(controller);
        }

        private static string GetSpeechText(SpeechBubbleController controller)
        {
            var labelField = typeof(SpeechBubbleController).GetField("_label", BindingFlags.Instance | BindingFlags.NonPublic);
            var label = labelField?.GetValue(controller);
            var textProperty = label?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            return textProperty?.GetValue(label) as string ?? string.Empty;
        }
    }
}
