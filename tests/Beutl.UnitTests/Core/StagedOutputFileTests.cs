using Beutl.IO;

namespace Beutl.UnitTests.Core;

public class StagedOutputFileTests
{
    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public void Only_a_successful_commit_replaces_the_existing_output(bool cancel)
    {
        string root = Directory.CreateTempSubdirectory("beutl-staged-output-").FullName;
        string destination = Path.Combine(root, "existing output.mp4");
        try
        {
            File.WriteAllText(destination, "old complete contents");
            using (var output = new StagedOutputFile(destination))
            {
                Assert.That(Path.GetDirectoryName(Path.GetDirectoryName(output.TemporaryPath)), Is.EqualTo(root));
                Assert.That(Path.GetFileName(output.TemporaryPath), Is.EqualTo(Path.GetFileName(destination)));
                File.WriteAllText(output.TemporaryPath, "new");
                Assert.That(File.ReadAllText(destination), Is.EqualTo("old complete contents"));
                if (cancel)
                    Assert.Throws<OperationCanceledException>(() => output.Commit(new CancellationToken(true)));
                else
                    output.Commit(CancellationToken.None);
            }

            Assert.That(File.ReadAllText(destination), Is.EqualTo(cancel ? "old complete contents" : "new"));
            Assert.That(Directory.GetDirectories(root), Is.Empty);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void A_missing_output_cannot_replace_an_existing_file()
    {
        string root = Directory.CreateTempSubdirectory("beutl-staged-output-").FullName;
        string destination = Path.Combine(root, "existing.mp4");
        try
        {
            File.WriteAllText(destination, "old");
            using (var output = new StagedOutputFile(destination))
                Assert.Throws<FileNotFoundException>(() => output.Commit(CancellationToken.None));

            Assert.That(File.ReadAllText(destination), Is.EqualTo("old"));
            Assert.That(Directory.GetDirectories(root), Is.Empty);
        }
        finally { Directory.Delete(root, true); }
    }
}
