using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Yuukei.Runtime;

namespace Yuukei.Tests.PlayMode
{
    public sealed class BootstrapPlayModeTests
    {
        [UnityTest]
        public IEnumerator SampleScene_CreatesResidentRuntime()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene");
            yield return null;
            yield return null;

            Assert.That(Object.FindFirstObjectByType<ResidentAppController>(), Is.Not.Null);
        }
    }
}
