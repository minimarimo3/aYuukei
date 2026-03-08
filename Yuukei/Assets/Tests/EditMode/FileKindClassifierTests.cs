using System;
using System.IO;
using NUnit.Framework;
using Yuukei.Runtime;

namespace Yuukei.Tests.EditMode
{
    public sealed class FileKindClassifierTests
    {
        [Test]
        public void Classify_UsesSpecBuckets()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "yuukei-filekind-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var imagePath = Path.Combine(tempDirectory, "icon.png");
            var docPath = Path.Combine(tempDirectory, "spec.pdf");
            var archivePath = Path.Combine(tempDirectory, "bundle.zip");
            var modelPath = Path.Combine(tempDirectory, "avatar.vrm");
            File.WriteAllText(imagePath, "x");
            File.WriteAllText(docPath, "x");
            File.WriteAllText(archivePath, "x");
            File.WriteAllText(modelPath, "x");

            try
            {
                Assert.That(FileKindClassifier.Classify(tempDirectory, true), Is.EqualTo("folder"));
                Assert.That(FileKindClassifier.Classify(imagePath, false), Is.EqualTo("image"));
                Assert.That(FileKindClassifier.Classify(docPath, false), Is.EqualTo("document"));
                Assert.That(FileKindClassifier.Classify(archivePath, false), Is.EqualTo("archive"));
                Assert.That(FileKindClassifier.Classify(modelPath, false), Is.EqualTo("model"));
                Assert.That(FileKindClassifier.Classify(Path.Combine(tempDirectory, "unknown.bin"), false), Is.EqualTo("other"));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }
}
