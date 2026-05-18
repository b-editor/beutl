namespace Beutl.UnitTests.Core;

public class BeutlEnvironmentTests
{
    [Test]
    public void GetHomeDirectoryPath_FallsBackToUserProfile_WhenEnvVarUnset()
    {
        string? original = Environment.GetEnvironmentVariable(BeutlEnvironment.HomeVariable);
        try
        {
            Environment.SetEnvironmentVariable(BeutlEnvironment.HomeVariable, null);
            string expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".beutl"
            );
            Assert.That(BeutlEnvironment.GetHomeDirectoryPath(), Is.EqualTo(expected));
        }
        finally
        {
            Environment.SetEnvironmentVariable(BeutlEnvironment.HomeVariable, original);
        }
    }

    [Test]
    public void GetHomeDirectoryPath_UsesEnvVar_WhenDirectoryExists()
    {
        string? original = Environment.GetEnvironmentVariable(BeutlEnvironment.HomeVariable);
        string tempDir = Path.Combine(Path.GetTempPath(), $"beutl-env-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            Environment.SetEnvironmentVariable(BeutlEnvironment.HomeVariable, tempDir);
            Assert.That(BeutlEnvironment.GetHomeDirectoryPath(), Is.EqualTo(tempDir));
        }
        finally
        {
            Environment.SetEnvironmentVariable(BeutlEnvironment.HomeVariable, original);
            Directory.Delete(tempDir);
        }
    }

    [Test]
    public void GetHomeDirectoryPath_FallsBackWhenEnvVarPathDoesNotExist()
    {
        string? original = Environment.GetEnvironmentVariable(BeutlEnvironment.HomeVariable);
        string nonExistent = Path.Combine(Path.GetTempPath(), $"beutl-missing-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable(BeutlEnvironment.HomeVariable, nonExistent);
            string expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".beutl"
            );
            Assert.That(BeutlEnvironment.GetHomeDirectoryPath(), Is.EqualTo(expected));
        }
        finally
        {
            Environment.SetEnvironmentVariable(BeutlEnvironment.HomeVariable, original);
        }
    }

    [Test]
    public void GetPackagesDirectoryPath_IsHomeSlashPackages()
    {
        string home = BeutlEnvironment.GetHomeDirectoryPath();
        Assert.That(
            BeutlEnvironment.GetPackagesDirectoryPath(),
            Is.EqualTo(Path.Combine(home, "packages"))
        );
    }

    [Test]
    public void GetSideloadsDirectoryPath_IsHomeSlashSideloads()
    {
        string home = BeutlEnvironment.GetHomeDirectoryPath();
        Assert.That(
            BeutlEnvironment.GetSideloadsDirectoryPath(),
            Is.EqualTo(Path.Combine(home, "sideloads"))
        );
    }
}
