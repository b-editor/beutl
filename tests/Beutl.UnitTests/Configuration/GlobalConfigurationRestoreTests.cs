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
            Assert.That(config.RestoreFailures.Select(f => f.Setting), Is.EqualTo(new[] { "Font.FontDirectories" }));
        });
    }

    [Test]
    public void A_null_keeps_the_default_of_a_setting_that_cannot_be_null()
    {
        // A JSON null reads without an error, and a null UICulture fails startup when it is applied.
        var defaults = new EditorConfig();
        GlobalConfiguration config = Restore(new JsonObject
        {
            ["View"] = new JsonObject { ["UICulture"] = null },
            ["Editor"] = new JsonObject
            {
                ["FrameCacheMaxSize"] = null,
                ["LastMediaDirectory"] = null,
            },
        });

        Assert.Multiple(() =>
        {
            Assert.That(config.ViewConfig.UICulture, Is.EqualTo(new ViewConfig().UICulture));
            Assert.That(config.EditorConfig.FrameCacheMaxSize, Is.EqualTo(defaults.FrameCacheMaxSize));
            Assert.That(config.EditorConfig.LastMediaDirectory, Is.Null);
            Assert.That(config.RestoreFailures.Select(f => f.Setting),
                Is.EqualTo(new[] { "View.UICulture", "Editor.FrameCacheMaxSize" }),
                "LastMediaDirectory is nullable, so null is its value, not a failure");
        });
    }

    [Test]
    public void A_section_that_is_not_an_object_is_reported_and_kept_as_a_backup()
    {
        var json = new JsonObject
        {
            ["Editor"] = "bad",
            ["ProxyStore"] = new JsonObject { ["DefaultPreset"] = 3 },
        };
        GlobalConfiguration config = Restore(json);

        Assert.That(config.ProxyStoreConfig.DefaultPreset, Is.EqualTo(3));
        Assert.That(config.RestoreFailures.Select(f => f.Setting), Is.EqualTo(new[] { "Editor" }));
        Assert.That(File.ReadAllText(SettingsPath + ".bak"), Is.EqualTo(json.ToJsonString()));
    }

    [Test]
    public void A_bad_list_keeps_the_values_a_section_reads_after_it()
    {
        GlobalConfiguration config = Restore(new JsonObject
        {
            ["View"] = new JsonObject
            {
                ["RecentFiles"] = "abc",
                ["RecentProjects"] = new JsonArray("/projects/a.bep"),
                ["WindowPosition"] = new JsonObject { ["X"] = 10, ["Y"] = 20 },
                ["WindowSize"] = new JsonObject { ["Width"] = 800, ["Height"] = 600 },
            },
        });

        Assert.Multiple(() =>
        {
            Assert.That(config.ViewConfig.RecentFiles, Is.Empty);
            Assert.That(config.ViewConfig.RecentProjects, Is.EqualTo(new[] { "/projects/a.bep" }));
            Assert.That(config.ViewConfig.WindowPosition, Is.EqualTo((10, 20)));
            Assert.That(config.ViewConfig.WindowSize, Is.EqualTo((800, 600)));
            Assert.That(config.RestoreFailures.Select(f => f.Setting), Is.EqualTo(new[] { "View.RecentFiles" }));
        });
    }

    [Test]
    public void A_bad_value_a_section_reads_in_its_own_shape_keeps_the_default()
    {
        // ViewConfig reads WindowPosition as an { X, Y } record itself.
        GlobalConfiguration config = Restore(new JsonObject
        {
            ["View"] = new JsonObject { ["WindowPosition"] = "abc" },
        });

        Assert.That(config.ViewConfig.WindowPosition, Is.Null);
        Assert.That(config.RestoreFailures.Select(f => f.Setting), Is.EqualTo(new[] { "View.WindowPosition" }));
    }

    [Test]
    public void A_file_with_unreadable_values_is_kept_as_a_backup_before_it_is_rewritten()
    {
        var json = new JsonObject { ["Editor"] = new JsonObject { ["FrameCacheMaxSize"] = "abc" } };
        GlobalConfiguration config = Restore(json);

        config.GraphicsConfig.SelectedGpuName = "other";
        config.Save(SettingsPath);

        Assert.That(config.RestoreBackupPath, Is.EqualTo(SettingsPath + ".bak"));
        Assert.That(File.ReadAllText(SettingsPath + ".bak"), Is.EqualTo(json.ToJsonString()));
    }

    [Test]
    public void A_backup_that_cannot_be_written_is_not_reported_as_kept()
    {
        // A directory in the way makes the copy fail, as a full disk or a read-only folder would.
        Directory.CreateDirectory(SettingsPath + ".bak");

        GlobalConfiguration config = Restore(new JsonObject { ["Editor"] = new JsonObject { ["FrameCacheMaxSize"] = "abc" } });

        Assert.That(config.RestoreFailures, Is.Not.Empty);
        Assert.That(config.RestoreBackupPath, Is.Null);
    }

    [Test]
    public void A_file_Beutl_saved_itself_restores_without_failures()
    {
        // Every setting at its default, nulls included, is a value the setting accepts.
        var saved = (GlobalConfiguration)Activator.CreateInstance(typeof(GlobalConfiguration), nonPublic: true)!;
        saved.Save(SettingsPath);

        var config = (GlobalConfiguration)Activator.CreateInstance(typeof(GlobalConfiguration), nonPublic: true)!;
        config.Restore(SettingsPath);

        Assert.That(config.RestoreFailures.Select(f => f.Setting), Is.Empty);
        Assert.That(File.Exists(SettingsPath + ".bak"), Is.False);
    }

    [Test]
    public void A_readable_file_leaves_no_backup()
    {
        GlobalConfiguration config = Restore(new JsonObject { ["Editor"] = new JsonObject { ["FrameCacheMaxSize"] = 512 } });

        Assert.That(config.RestoreFailures, Is.Empty);
        Assert.That(config.RestoreBackupPath, Is.Null);
        Assert.That(File.Exists(SettingsPath + ".bak"), Is.False);
    }
}
