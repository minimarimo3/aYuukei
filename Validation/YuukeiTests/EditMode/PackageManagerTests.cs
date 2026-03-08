using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Newtonsoft.Json;
using Yuukei.Runtime;

namespace Yuukei.Tests.EditMode
{
    public sealed class PackageManagerTests
    {
        [Test]
        public void ValidateActivePackage_SkipsBrokenElementsButReportsWarnings()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "yuukei-package-" + Guid.NewGuid().ToString("N"));
            var packageRoot = Path.Combine(tempDirectory, "package");
            var installRoot = Path.Combine(packageRoot, "creator-v1-guid");
            Directory.CreateDirectory(installRoot);

            var manifest = new PackageManifest
            {
                Creator = "creator",
                Version = "v1",
                Id = "guid",
                Daihon = "Scripts/main.daihon",
                Character = "character.vrm",
                Textures = new Dictionary<string, PackageTextureManifest>
                {
                    ["speechBubble"] = new PackageTextureManifest
                    {
                        Background = "Textures/bg.png",
                        Tail = "Textures/tail.png",
                    }
                },
                Dlls = new System.Collections.Generic.List<string> { "Plugins/feature.dll" },
            };

            File.WriteAllText(Path.Combine(installRoot, "manifest.json"), JsonConvert.SerializeObject(manifest, Formatting.Indented));
            Directory.CreateDirectory(Path.Combine(installRoot, "Scripts"));
            File.WriteAllText(Path.Combine(installRoot, "Scripts", "main.daihon"), "### Test\n合図: ＠app_started\n「hello」");

            try
            {
                var store = new PersistenceStore(Path.Combine(tempDirectory, "save.json"));
                var packageManager = new PackageManager(store, packageRootDirectory: packageRoot);
                packageManager.ReloadInstalledPackagesAsync().AsTask().GetAwaiter().GetResult();
                packageManager.SwitchActivePackageAsync("guid").AsTask().GetAwaiter().GetResult();

                var report = packageManager.ValidateActivePackage();

                Assert.That(report.Warnings.Count, Is.GreaterThanOrEqualTo(3));
                Assert.That(report.Warnings[0], Does.Contain("Missing"));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }
}
