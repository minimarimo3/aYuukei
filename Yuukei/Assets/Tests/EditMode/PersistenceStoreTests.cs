using System;
using System.IO;
using NUnit.Framework;
using Yuukei.Runtime;

namespace Yuukei.Tests.EditMode
{
    public sealed class PersistenceStoreTests
    {
        [Test]
        public void SaveRoundTrip_PreservesShape()
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
                store.SaveAsync().GetAwaiter().GetResult();

                var reloaded = new PersistenceStore(savePath);
                reloaded.LoadAsync().GetAwaiter().GetResult();

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
    }
}
