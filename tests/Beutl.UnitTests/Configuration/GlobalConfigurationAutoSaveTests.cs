using Beutl.Configuration;
using Beutl.Testing.Headless;

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
        string path = GlobalConfiguration.DefaultFilePath;
        Assert.That(
            path,
            Is.EqualTo(Path.Combine(BeutlHomeIsolation.CurrentHome!, "settings.json")),
            "AssemblySetUp must isolate BEUTL_HOME so a regression cannot touch the real settings.");
        const string Sentinel = "{\"sentinel\":true}";
        File.WriteAllText(path, Sentinel);
        try
        {
            GlobalConfiguration config = CreateConfiguration();

            config.FontConfig.FontDirectories.Clear();

            Assert.That(File.ReadAllText(path), Is.EqualTo(Sentinel));
        }
        finally
        {
            File.Delete(path);
        }
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
