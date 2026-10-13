using Beutl.Configuration;

using NUnit.Framework;

namespace Beutl.UnitTests.Configuration;

// A temp folder for the settings.json that a fixture restores a GlobalConfiguration from.
public abstract class GlobalConfigurationFileFixture(string directoryPrefix)
{
    protected string DirectoryPath { get; private set; } = null!;

    protected string SettingsPath => Path.Combine(DirectoryPath, "settings.json");

    [SetUp]
    public void SetUp() => DirectoryPath = Directory.CreateTempSubdirectory(directoryPrefix).FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(DirectoryPath, recursive: true);

    protected static GlobalConfiguration CreateConfiguration()
        => (GlobalConfiguration)Activator.CreateInstance(typeof(GlobalConfiguration), nonPublic: true)!;

    protected GlobalConfiguration Restore(string text)
    {
        File.WriteAllText(SettingsPath, text);
        GlobalConfiguration config = CreateConfiguration();
        config.Restore(SettingsPath);
        return config;
    }
}
