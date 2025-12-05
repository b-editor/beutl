using System;
using System.IO;

namespace Beutl.UnitTests.Core;

public class BeutlEnvironmentTests
{
    [Test]
    public void HomeDirectory_UsesEnvVarWhenExists()
    {
        string dir = Path.Combine(ArtifactProvider.GetArtifactDirectory(), "home");
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable(BeutlEnvironment.HomeVariable, dir);

        string home = BeutlEnvironment.GetHomeDirectoryPath();
        Assert.That(home, Is.EqualTo(dir));
    }

    [Test]
    public void HomeDirectory_FallsBackWhenEnvMissingOrNotExists()
    {
        // Set to a non-existing directory
        string dir = Path.Combine(ArtifactProvider.GetArtifactDirectory(), "missing-home");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Environment.SetEnvironmentVariable(BeutlEnvironment.HomeVariable, dir);

        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".beutl");
        string home = BeutlEnvironment.GetHomeDirectoryPath();
        Assert.That(home, Is.EqualTo(expected));
    }

    [Test]
    public void PackagesAndSideloads_CombineCorrectly()
    {
        string dir = Path.Combine(ArtifactProvider.GetArtifactDirectory(), "home2");
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable(BeutlEnvironment.HomeVariable, dir);

        Assert.That(BeutlEnvironment.GetPackagesDirectoryPath(), Is.EqualTo(Path.Combine(dir, "packages")));
        Assert.That(BeutlEnvironment.GetSideloadsDirectoryPath(), Is.EqualTo(Path.Combine(dir, "sideloads")));
    }
}

