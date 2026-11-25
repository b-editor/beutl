using System.Globalization;
using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Serialization;
using NUnit.Framework;

namespace Beutl.UnitTests.Configuration;

// settings.json can parse as JSON and still hold a value of the wrong type: a hand edit, a sync tool
// merging two copies, or a build that typed the setting differently. Restoring it must not stop Beutl
// from starting, and must not throw away the settings that are fine.
[TestFixture]
public class GlobalConfigurationRestoreTests() : GlobalConfigurationFileFixture("global-config-restore-")
{
    private GlobalConfiguration Restore(JsonObject json) => Restore(json.ToJsonString());

    [TestCase(null)]
    [TestCase("{}")]
    [TestCase("""{"Update":{"AutoDownload":false}}""")]
    public void Extension_updates_default_to_always_when_no_preference_is_saved(string? settings)
    {
        if (settings is not null)
            File.WriteAllText(SettingsPath, settings);

        var config = CreateConfiguration();
        config.Restore(SettingsPath);
        Assert.That(config.UpdateConfig.ExtensionUpdates.Value, Is.EqualTo(ExtensionUpdateMode.Always));
    }

    [TestCase(ExtensionUpdateMode.NotifyOnly)]
    [TestCase(ExtensionUpdateMode.WithAppUpdate)]
    public void Extension_updates_keep_the_saved_preference(ExtensionUpdateMode mode)
    {
        var config = Restore("{}");
        config.UpdateConfig.ExtensionUpdates.Value = mode;

        var reopened = CreateConfiguration();
        reopened.Restore(SettingsPath);
        Assert.That(reopened.UpdateConfig.ExtensionUpdates.Value, Is.EqualTo(mode));
    }

    [Test]
    public void Terminal_settings_default_for_older_files_and_auto_save_edits()
    {
        TerminalConfig settings = Restore(new JsonObject()).TerminalConfig;
        Assert.That(settings.FontSize.Value, Is.EqualTo(14));
        Assert.That(settings.ScrollbackLines.Value, Is.EqualTo(5000));
        Assert.That(settings.ColorScheme.Value, Is.EqualTo(TerminalColorScheme.FollowApplication));
        Assert.That(settings.ShellPath.Value, Is.Null);

        settings.FontSize.Value = 18;
        settings.FontFamily.Value = "My Mono";
        settings.ColorScheme.Value = TerminalColorScheme.Light;
        settings.ScrollbackLines.Value = 0;
        settings.ShellPath.Value = "/path with spaces/pwsh";
        settings.ShellArguments.Value = "-File\nmy script.ps1";

        GlobalConfiguration reopened = CreateConfiguration();
        reopened.Restore(SettingsPath);
        Assert.That(reopened.RestoreFailures, Is.Empty);
        Assert.That(reopened.TerminalConfig.FontFamily.Value, Is.EqualTo("My Mono"));
        Assert.That(reopened.TerminalConfig.FontSize.Value, Is.EqualTo(18));
        Assert.That(reopened.TerminalConfig.ColorScheme.Value, Is.EqualTo(TerminalColorScheme.Light));
        Assert.That(reopened.TerminalConfig.ScrollbackLines.Value, Is.Zero);
        Assert.That(reopened.TerminalConfig.ShellPath.Value, Is.EqualTo("/path with spaces/pwsh"));
        Assert.That(reopened.TerminalConfig.ShellArguments.Value, Is.EqualTo("-File\nmy script.ps1"));
    }

    [Test]
    public void A_removed_setting_in_an_older_file_is_ignored_and_not_saved_again()
    {
        // ShowExactBoundaries was removed with the preview's selection outline (#4608).
        GlobalConfiguration config = Restore(new JsonObject
        {
            ["View"] = new JsonObject
            {
                ["ShowExactBoundaries"] = true,
                ["UseCustomAccentColor"] = true,
            },
        });

        Assert.Multiple(() =>
        {
            Assert.That(config.ViewConfig.UseCustomAccentColor.Value, Is.True);
            Assert.That(config.RestoreFailures, Is.Empty);
            Assert.That(CoreSerializer.SerializeToJsonObject(config.ViewConfig).ContainsKey("ShowExactBoundaries"), Is.False,
                "the retired key is not written back");
        });
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
                ["IsRippleEnabled"] = !defaults.IsRippleEnabled.Value,
            },
            ["ProxyStore"] = new JsonObject { ["DefaultPreset"] = 3 },
        });

        Assert.Multiple(() =>
        {
            Assert.That(config.EditorConfig.FrameCacheMaxSize.Value, Is.EqualTo(defaults.FrameCacheMaxSize.Value));
            Assert.That(config.EditorConfig.IsRippleEnabled.Value, Is.EqualTo(!defaults.IsRippleEnabled.Value), "the same section's other settings are read");
            Assert.That(config.ProxyStoreConfig.DefaultPreset.Value, Is.EqualTo(3), "later sections are read");
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
                ["UseNativeContextMenus"] = !defaults.UseNativeContextMenus.Value,
            },
            ["Editor"] = new JsonObject { ["LastMediaDirectory"] = 2 },
            ["ProxyStore"] = new JsonObject { ["DefaultPreset"] = 3 },
        });

        Assert.Multiple(() =>
        {
            Assert.That(config.ViewConfig.UICulture.Value, Is.EqualTo(defaults.UICulture.Value));
            Assert.That(config.ViewConfig.UseNativeContextMenus.Value, Is.EqualTo(!defaults.UseNativeContextMenus.Value));
            Assert.That(config.EditorConfig.LastMediaDirectory.Value, Is.Null);
            Assert.That(config.ProxyStoreConfig.DefaultPreset.Value, Is.EqualTo(3));
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
            Assert.That(config.ViewConfig.UICulture.Value, Is.EqualTo(CultureInfo.GetCultureInfo("ja-JP")));
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
            Assert.That(config.ViewConfig.UICulture.Value, Is.EqualTo(new ViewConfig().UICulture.Value));
            Assert.That(config.EditorConfig.FrameCacheMaxSize.Value, Is.EqualTo(defaults.FrameCacheMaxSize.Value));
            Assert.That(config.EditorConfig.LastMediaDirectory.Value, Is.Null);
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

        Assert.That(config.ProxyStoreConfig.DefaultPreset.Value, Is.EqualTo(3));
        Assert.That(config.RestoreFailures.Select(f => f.Setting), Is.EqualTo(new[] { "Editor" }));
        Assert.That(File.ReadAllText(SettingsPath + ".bak"), Is.EqualTo(json.ToJsonString()));
    }

    [Test]
    public void A_null_entry_in_the_editor_extensions_keeps_the_rest_of_the_section()
    {
        GlobalConfiguration config = Restore(new JsonObject
        {
            ["Extension"] = new JsonObject
            {
                ["EditorExtensions"] = new JsonObject { [".mp4"] = null, [".png"] = new JsonArray(null, "[Beutl.Engine]Beutl.Graphics.Bitmap") },
                ["DecoderPriority"] = new JsonArray("[Beutl.Engine]Beutl.Graphics.Bitmap"),
            },
        });

        Assert.Multiple(() =>
        {
            Assert.That(config.ExtensionConfig.EditorExtensions[".mp4"], Is.Empty);
            Assert.That(config.ExtensionConfig.EditorExtensions[".png"], Has.Count.EqualTo(1));
            Assert.That(config.ExtensionConfig.DecoderPriority, Has.Count.EqualTo(1), "the value after the null entry is read");
            Assert.That(config.RestoreFailures, Is.Empty);
        });
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
            Assert.That(config.ViewConfig.WindowPosition.Value, Is.EqualTo((10, 20)));
            Assert.That(config.ViewConfig.WindowSize.Value, Is.EqualTo((800, 600)));
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

        Assert.That(config.ViewConfig.WindowPosition.Value, Is.Null);
        Assert.That(config.RestoreFailures.Select(f => f.Setting), Is.EqualTo(new[] { "View.WindowPosition" }));
    }

    [Test]
    public void A_file_with_unreadable_values_is_kept_as_a_backup_before_it_is_rewritten()
    {
        var json = new JsonObject { ["Editor"] = new JsonObject { ["FrameCacheMaxSize"] = "abc" } };
        GlobalConfiguration config = Restore(json);

        config.GraphicsConfig.SelectedGpuName.Value = "other";
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
        var saved = new GlobalConfiguration();
        saved.Save(SettingsPath);

        var config = new GlobalConfiguration();
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
