using System;
using System.IO;
using NUnit.Framework;
using Daihon;
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
                store.Data.Overrides.Daihon = "Scripts/a.daihon";
                store.Data.Overrides.Textures["speechBubble.background"] = "Textures/a.png";
                store.SetPersistentVariable("flag", true);
                store.SetPersistentVariable("count", 12d);
                store.SetPersistentVariable("name", "Yuukei");
                store.SaveAsync().AsTask().GetAwaiter().GetResult();

                var reloaded = new PersistenceStore(savePath);
                reloaded.LoadAsync().AsTask().GetAwaiter().GetResult();

                Assert.That(reloaded.Data.ActivePackageId, Is.EqualTo("package-a"));
                Assert.That(reloaded.Data.Overrides.Daihon, Is.EqualTo("Scripts/a.daihon"));
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
        public void VariableStore_ResetsTemporaryAndEventContext()
        {
            var store = new PersistenceStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
            var variableStore = new YuukeiVariableStore(store);

            variableStore.SetValue("_temp", DaihonValue.FromNumber(1));
            variableStore.InjectEventContext(new System.Collections.Generic.Dictionary<string, object>
            {
                ["_event_x"] = 10d,
                ["_event_character_id"] = "default_mascot",
            });
            variableStore.SetValue("persistent_name", DaihonValue.FromString("kept"));

            variableStore.ResetTransientState();

            Assert.That(variableStore.IsDefined("_temp"), Is.False);
            Assert.That(variableStore.IsDefined("_event_x"), Is.False);
            Assert.That(variableStore.IsDefined("persistent_name"), Is.True);
        }
    }
}
