using NUnit.Framework;
using UnityEngine.TestTools;
using Yuukei.Runtime;

namespace Yuukei.Tests.EditMode
{
    public sealed class AliasRegistryTests
    {
        [Test]
        public void BuiltinAliases_AreResolved()
        {
            var registry = new AliasRegistry();

            Assert.That(registry.TryResolveEventName("クリック", out var eventName), Is.True);
            Assert.That(eventName, Is.EqualTo("character_clicked"));

            Assert.That(registry.TryResolveFunctionName("吹き出し表示", out var functionName), Is.True);
            Assert.That(functionName, Is.EqualTo("show_dialog"));
        }

        [Test]
        public void CanonicalNames_FallBackWithAsciiCaseInsensitiveMatch()
        {
            var registry = new AliasRegistry();

            Assert.That(registry.TryResolveEventName("APP_STARTED", out var eventName), Is.True);
            Assert.That(eventName, Is.EqualTo("app_started"));

            Assert.That(registry.TryResolveFunctionName("SHOW_DIALOG", out var functionName), Is.True);
            Assert.That(functionName, Is.EqualTo("show_dialog"));
        }

        [Test]
        public void PackageAliases_OverrideBuiltins_AndLogCollision()
        {
            var registry = new AliasRegistry();
            var manifest = new PackageAliasManifest();
            manifest.Events["クリック"] = "app_started";

            LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("alias collision"));
            registry.LoadPackageAliases(manifest);

            Assert.That(registry.TryResolveEventName("クリック", out var eventName), Is.True);
            Assert.That(eventName, Is.EqualTo("app_started"));
        }
    }
}
