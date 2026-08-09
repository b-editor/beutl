using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

public sealed class FilePathBoundaryTests
{
    [Test]
    public void IsPathInsideRoot_UsesPlatformPathCaseSemantics()
    {
        string root = Path.Combine(Path.GetTempPath(), "Beutl-Root");
        string differentlyCasedPath = Path.Combine(
            Path.GetTempPath(),
            "beutl-root",
            "sidecar.json");

        Assert.That(
            FilePathBoundary.IsPathInsideRoot(root, differentlyCasedPath),
            Is.EqualTo(OperatingSystem.IsWindows()));
    }
}
