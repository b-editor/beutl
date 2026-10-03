using Beutl.Configuration;

using NUnit.Framework;

namespace Beutl.UnitTests.Configuration;

[TestFixture]
public class GlobalConfigurationAutoSaveTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("global-config-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    private static GlobalConfiguration CreateConfiguration()
        => (GlobalConfiguration)Activator.CreateInstance(typeof(GlobalConfiguration), nonPublic: true)!;

    [Test]
    public void Change_before_restore_does_not_write_the_default_settings_file()
    {
        // A process that never loaded the user's settings holds defaults; auto-saving them would
        // replace the user's settings.json with a reset copy.
        GlobalConfiguration config = CreateConfiguration();
        bool existed = File.Exists(GlobalConfiguration.DefaultFilePath);
        DateTime before = existed ? File.GetLastWriteTimeUtc(GlobalConfiguration.DefaultFilePath) : default;

        config.FontConfig.FontDirectories.Clear();

        Assert.That(File.Exists(GlobalConfiguration.DefaultFilePath), Is.EqualTo(existed));
        if (existed)
            Assert.That(File.GetLastWriteTimeUtc(GlobalConfiguration.DefaultFilePath), Is.EqualTo(before));
    }

    [Test]
    public void Change_after_restore_saves_to_the_restored_file()
    {
        string path = Path.Combine(_directory, "settings.json");
        GlobalConfiguration config = CreateConfiguration();
        config.Restore(path);

        config.FontConfig.FontDirectories.Add("/restored-font-directory");

        Assert.That(File.ReadAllText(path), Does.Contain("/restored-font-directory"));
    }
}
