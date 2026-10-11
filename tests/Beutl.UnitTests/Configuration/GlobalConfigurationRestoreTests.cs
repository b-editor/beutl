using System.Globalization;
using System.Text.Json.Nodes;
using Beutl.Configuration;

using NUnit.Framework;

namespace Beutl.UnitTests.Configuration;

// settings.json can parse as JSON and still hold a value of the wrong type: a hand edit, a sync tool
// merging two copies, or a build that typed the setting differently. Restoring it must not stop Beutl
// from starting, and must not throw away the settings that are fine.
[TestFixture]
public class GlobalConfigurationRestoreTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("global-config-restore-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    private GlobalConfiguration Restore(JsonObject json)
    {
        File.WriteAllText(SettingsPath, json.ToJsonString());
        var config = (GlobalConfiguration)Activator.CreateInstance(typeof(GlobalConfiguration), nonPublic: true)!;
        config.Restore(SettingsPath);
        return config;
    }

    [Test]
    public void A_string_where_a_number_belongs_keeps_the_default_and_the_rest_of_the_file()
    {
        var defaults = new EditorConfig();
        GlobalConfiguration config = Restore(new JsonObject
        {
            ["Editor"] = new JsonObject
            {
                ["FrameCacheMaxSize"] = "abc",
                ["IsRippleEnabled"] = !defaults.IsRippleEnabled,
            },
            ["ProxyStore"] = new JsonObject { ["DefaultPreset"] = 3 },
        });

        Assert.Multiple(() =>
        {
            Assert.That(config.EditorConfig.FrameCacheMaxSize, Is.EqualTo(defaults.FrameCacheMaxSize));
            Assert.That(config.EditorConfig.IsRippleEnabled, Is.EqualTo(!defaults.IsRippleEnabled), "the same section's other settings are read");
            Assert.That(config.ProxyStoreConfig.DefaultPreset, Is.EqualTo(3), "later sections are read");
            Assert.That(config.RestoreFailures.Select(f => f.Setting), Is.EqualTo(new[] { "Editor.FrameCacheMaxSize" }));
        });
    }

    [Test]
    public void A_number_where_a_string_belongs_keeps_the_default_and_the_rest_of_the_file()
    {
        var defaults = new ViewConfig();
        GlobalConfiguration config = Restore(new JsonObject
        {
            ["View"] = new JsonObject
            {
                ["UICulture"] = 1,
                ["ShowExactBoundaries"] = !defaults.ShowExactBoundaries,
            },
            ["Editor"] = new JsonObject { ["LastMediaDirectory"] = 2 },
            ["ProxyStore"] = new JsonObject { ["DefaultPreset"] = 3 },
        });

        Assert.Multiple(() =>
        {
            Assert.That(config.ViewConfig.UICulture, Is.EqualTo(defaults.UICulture));
            Assert.That(config.ViewConfig.ShowExactBoundaries, Is.EqualTo(!defaults.ShowExactBoundaries));
            Assert.That(config.EditorConfig.LastMediaDirectory, Is.Null);
            Assert.That(config.ProxyStoreConfig.DefaultPreset, Is.EqualTo(3));
            Assert.That(config.RestoreFailures.Select(f => f.Setting),
                Is.EqualTo(new[] { "View.UICulture", "Editor.LastMediaDirectory" }));
        });
    }

    [Test]
    public void A_string_where_a_list_belongs_keeps_the_default_and_the_later_sections()
    {
        GlobalConfiguration config = Restore(new JsonObject
        {
            ["Font"] = new JsonObject { ["FontDirectories"] = "abc" },
            ["View"] = new JsonObject { ["UICulture"] = "ja-JP" },
        });

        Assert.Multiple(() =>
        {
            Assert.That(config.FontConfig.FontDirectories, Is.EqualTo(new FontConfig().FontDirectories));
            Assert.That(config.ViewConfig.UICulture, Is.EqualTo(CultureInfo.GetCultureInfo("ja-JP")));
            Assert.That(config.RestoreFailures.Select(f => f.Setting), Is.EqualTo(new[] { "Font" }));
        });
    }

    [Test]
    public void A_file_with_unreadable_values_is_kept_as_a_backup_before_it_is_rewritten()
    {
        var json = new JsonObject { ["Editor"] = new JsonObject { ["FrameCacheMaxSize"] = "abc" } };
        GlobalConfiguration config = Restore(json);

        config.GraphicsConfig.SelectedGpuName = "other";
        config.Save(SettingsPath);

        Assert.That(File.ReadAllText(SettingsPath + ".bak"), Is.EqualTo(json.ToJsonString()));
    }

    [Test]
    public void A_readable_file_leaves_no_backup()
    {
        GlobalConfiguration config = Restore(new JsonObject { ["Editor"] = new JsonObject { ["FrameCacheMaxSize"] = 512 } });

        Assert.That(config.RestoreFailures, Is.Empty);
        Assert.That(File.Exists(SettingsPath + ".bak"), Is.False);
    }
}
