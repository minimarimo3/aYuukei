using System;
using System.IO;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Yuukei.Runtime;

namespace Yuukei.Tests.EditMode
{
    public sealed class PersistenceStoreTests
    {
        [Test]
        public async Task SaveRoundTrip_PreservesShape()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "yuukei-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var savePath = Path.Combine(tempDirectory, "save.json");

            try
            {
                var store = new PersistenceStore(savePath);
                store.SetActivePackageId("package-a");
                store.Data.Overrides.Daihon.Add("Scripts/a.daihon");
                store.Data.Overrides.Daihon.Add("Scripts/b.daihon");
                store.Data.Overrides.Textures["speechBubble.background"] = "Textures/a.png";
                store.SetPersistentVariable("flag", true);
                store.SetPersistentVariable("count", 12d);
                store.SetPersistentVariable("name", "Yuukei");
                await store.SaveAsync();

                var reloaded = new PersistenceStore(savePath);
                await reloaded.LoadAsync();

                Assert.That(reloaded.Data.ActivePackageId, Is.EqualTo("package-a"));
                Assert.That(reloaded.Data.Overrides.Daihon, Is.EqualTo(new[] { "Scripts/a.daihon", "Scripts/b.daihon" }));
                Assert.That(reloaded.Data.Overrides.Textures["speechBubble.background"], Is.EqualTo("Textures/a.png"));
                Assert.That(reloaded.Data.PersistentVariables["flag"], Is.EqualTo(true));
                Assert.That(reloaded.Data.PersistentVariables["count"], Is.EqualTo(12d));
                Assert.That(reloaded.Data.PersistentVariables["name"], Is.EqualTo("Yuukei"));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public void PersistentVariables_RejectTypeChanges()
        {
            var store = new PersistenceStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
            store.SetPersistentVariable("answer", 42d);

            var exception = Assert.Throws<InvalidOperationException>(() => store.SetPersistentVariable("answer", "forty-two"));
            Assert.That(exception?.Message, Does.Contain("cannot change type"));
        }

        [Test]
        public async Task RequestSave_AndFlushPendingSave_PersistChanges()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "yuukei-savequeue-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var savePath = Path.Combine(tempDirectory, "save.json");

            try
            {
                var store = new PersistenceStore(savePath);
                store.SetActivePackageId("queued-package");
                store.RequestSave();
                await store.FlushPendingSaveAsync();

                var reloaded = new PersistenceStore(savePath);
                await reloaded.LoadAsync();

                Assert.That(reloaded.Data.ActivePackageId, Is.EqualTo("queued-package"));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public async Task RequestSave_AndSaveImmediately_PersistChanges()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "yuukei-savesync-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var savePath = Path.Combine(tempDirectory, "save.json");

            try
            {
                var store = new PersistenceStore(savePath);
                store.SetActivePackageId("sync-package");
                store.RequestSave();
                store.SaveImmediately();

                var reloaded = new PersistenceStore(savePath);
                await reloaded.LoadAsync();

                Assert.That(reloaded.Data.ActivePackageId, Is.EqualTo("sync-package"));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public async Task LoadAsync_IgnoresUnsupportedPersistentVariableTypes()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "yuukei-badpersist-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var savePath = Path.Combine(tempDirectory, "save.json");
            File.WriteAllText(savePath,
@"{
  ""activePackageId"": ""package-a"",
  ""persistentVariables"": {
    ""ok"": true,
    ""badObject"": { ""nested"": true },
    ""badArray"": [1, 2, 3]
  }
}");

            try
            {
                var store = new PersistenceStore(savePath);
                await store.LoadAsync();

                Assert.That(store.Data.PersistentVariables.ContainsKey("ok"), Is.True);
                Assert.That(store.Data.PersistentVariables.ContainsKey("badObject"), Is.False);
                Assert.That(store.Data.PersistentVariables.ContainsKey("badArray"), Is.False);
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }
}
