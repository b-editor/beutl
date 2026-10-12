using System.Globalization;
using System.Text.Json.Nodes;
using Beutl.Configuration;

using NUnit.Framework;

namespace Beutl.UnitTests.Configuration;

// Every Beutl build on a machine shares settings.json, and any settings change rewrites it. A build writes back
// the sections it does not know, so an older build keeps what a newer one added, and it copies a file it cannot
// read aside before a save replaces it.
[TestFixture]
public class GlobalConfigurationKeepTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("global-config-keep-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    private static GlobalConfiguration CreateConfiguration()
        => (GlobalConfiguration)Activator.CreateInstance(typeof(GlobalConfiguration), nonPublic: true)!;

    private GlobalConfiguration Restore(string text)
    {
        File.WriteAllText(SettingsPath, text);
        GlobalConfiguration config = CreateConfiguration();
        config.Restore(SettingsPath);
        return config;
    }

    private JsonObject ReadSaved() => JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();

    private string[] UnreadableCopies() => Directory.GetFiles(_directory, "settings.json.corrupt-*");

    [Test]
    public void Sections_this_build_does_not_know_are_written_back_when_a_setting_changes()
    {
        // As a newer build, or an extension that is not loaded, would write them.
        var timeline = new JsonObject
        {
            ["Snap"] = true,
            ["Guides"] = new JsonArray(1.5, "日本語", new JsonObject { ["Nested"] = null }),
        };
        GlobalConfiguration config = Restore(new JsonObject
        {
            ["Timeline"] = timeline.DeepClone(),
            ["Graphics"] = new JsonObject { ["SelectedGpuName"] = "before" },
            ["Flag"] = false,
            ["Cleared"] = null,
        }.ToJsonString());

        config.GraphicsConfig.SelectedGpuName = "after";

        JsonObject saved = ReadSaved();
        Assert.Multiple(() =>
        {
            Assert.That(config.RestoreFileFailure, Is.Null);
            Assert.That(UnreadableCopies(), Is.Empty);
            Assert.That(JsonNode.DeepEquals(saved["Timeline"], timeline), Is.True, saved.ToJsonString());
            Assert.That(saved["Flag"]?.GetValue<bool>(), Is.False);
            Assert.That(saved.TryGetPropertyValue("Cleared", out JsonNode? cleared) && cleared is null, Is.True,
                "a null section is kept too");
            Assert.That(saved["Graphics"]?["SelectedGpuName"]?.GetValue<string>(), Is.EqualTo("after"));
        });
    }

    // A section it knows also writes back the settings in it that this build does not know.
    [Test]
    public void Settings_a_known_section_does_not_know_are_written_back_when_a_setting_changes()
    {
        var future = new JsonObject { ["Mode"] = "Fast", ["Levels"] = new JsonArray(1, 2) };
        GlobalConfiguration config = Restore(new JsonObject
        {
            ["Graphics"] = new JsonObject { ["SelectedGpuName"] = "before", ["FutureSetting"] = future.DeepClone() },
        }.ToJsonString());

        config.GraphicsConfig.SelectedGpuName = "after";

        JsonObject saved = ReadSaved();
        Assert.Multiple(() =>
        {
            Assert.That(saved["Graphics"]?["SelectedGpuName"]?.GetValue<string>(), Is.EqualTo("after"));
            Assert.That(JsonNode.DeepEquals(saved["Graphics"]?["FutureSetting"], future), Is.True,
                saved.ToJsonString());
        });
    }

    [Test]
    public void Kept_sections_follow_the_known_ones_in_the_order_the_file_had_them()
    {
        GlobalConfiguration config = Restore(new JsonObject
        {
            ["Zeta"] = 1,
            ["Font"] = new JsonObject(),
            ["Alpha"] = new JsonObject { ["Value"] = 2 },
        }.ToJsonString());
        config.Save(SettingsPath);
        string first = File.ReadAllText(SettingsPath);

        GlobalConfiguration next = CreateConfiguration();
        next.Restore(SettingsPath);
        next.Save(SettingsPath);

        string[] keys = ReadSaved().Select(p => p.Key).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(keys[0], Is.EqualTo("Version"));
            Assert.That(keys[^2..], Is.EqualTo(new[] { "Zeta", "Alpha" }));
            Assert.That(File.ReadAllText(SettingsPath), Is.EqualTo(first), "saving what a save wrote writes the same file");
        });
    }

    [Test]
    public void A_lower_case_section_an_early_build_wrote_is_read_but_not_written_back()
    {
        // Builds before 1.0.0-preview.2 wrote lower-case keys, which are read in place of the current key when
        // present, so writing one back would hide every later change to its section.
        GlobalConfiguration config = Restore(new JsonObject
        {
            ["view"] = new JsonObject { ["UICulture"] = "ja-JP" },
        }.ToJsonString());
        Assert.That(config.ViewConfig.UICulture, Is.EqualTo(CultureInfo.GetCultureInfo("ja-JP")));

        config.ViewConfig.UICulture = CultureInfo.GetCultureInfo("en-US");
        config.Save(SettingsPath);

        GlobalConfiguration next = CreateConfiguration();
        next.Restore(SettingsPath);
        Assert.Multiple(() =>
        {
            Assert.That(ReadSaved().ContainsKey("view"), Is.False);
            Assert.That(next.ViewConfig.UICulture, Is.EqualTo(CultureInfo.GetCultureInfo("en-US")));
        });
    }

    [Test]
    public void Version_names_the_build_that_saved_the_file()
    {
        // A newer build's version is read as the version started last, and this build writes its own.
        GlobalConfiguration config = Restore(new JsonObject { ["Version"] = "99.0.0" }.ToJsonString());
        config.Save(SettingsPath);

        Assert.Multiple(() =>
        {
            Assert.That(config.LastStartedVersion, Is.EqualTo("99.0.0"));
            Assert.That(ReadSaved()["Version"]?.GetValue<string>(), Is.EqualTo(BeutlApplication.Version));
        });
    }

    [TestCase("{\"Graphics\": {\"SelectedGpuName\": \"cut off", TestName = "{m}(cut off)")]
    [TestCase("{\"Editor\": {}, \"Editor\": {}}", TestName = "{m}(repeated key)")]
    [TestCase("[\"Editor\"]", TestName = "{m}(not an object)")]
    [TestCase("null", TestName = "{m}(null)")]
    [TestCase("", TestName = "{m}(empty)")]
    public void A_file_that_cannot_be_read_is_copied_aside_before_a_save_replaces_it(string text)
    {
        GlobalConfiguration config = Restore(text);

        Assert.That(config.RestoreFileFailure, Is.Not.Null);
        Assert.That(config.RestoreBackupPath, Is.Not.Null, "the file is copied aside");
        string copy = config.RestoreBackupPath!;
        Assert.Multiple(() =>
        {
            Assert.That(Path.GetFileName(copy), Does.Match(@"^settings\.json\.corrupt-\d{14}-[0-9a-f]{8}$"));
            Assert.That(File.ReadAllText(copy), Is.EqualTo(text));
        });

        config.GraphicsConfig.SelectedGpuName = "after";

        GlobalConfiguration next = CreateConfiguration();
        next.Restore(SettingsPath);
        Assert.Multiple(() =>
        {
            Assert.That(next.RestoreFileFailure, Is.Null);
            Assert.That(next.RestoreFailures, Is.Empty);
            Assert.That(next.GraphicsConfig.SelectedGpuName, Is.EqualTo("after"));
            Assert.That(File.ReadAllText(copy), Is.EqualTo(text), "the save leaves the copy alone");
            Assert.That(UnreadableCopies(), Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void The_copy_of_a_file_that_cannot_be_read_is_as_private_as_the_file()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file modes do not apply on Windows.");
            return;
        }

        // A pending migration can leave a live MCP token in settings.json, which Save writes for the user only.
        const UnixFileMode UserOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.WriteAllText(SettingsPath, "{\"AiAgent\": ");
        File.SetUnixFileMode(SettingsPath, UserOnly);
        GlobalConfiguration config = CreateConfiguration();
        config.Restore(SettingsPath);

        Assert.That(config.RestoreBackupPath, Is.Not.Null);
        Assert.That(File.GetUnixFileMode(config.RestoreBackupPath!), Is.EqualTo(UserOnly));
    }

    [Test]
    public void A_file_that_cannot_be_read_or_copied_aside_is_not_saved_over()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Keeping the copy from being written takes an ACL change on Windows.");
            return;
        }

        const string Text = "{\"Graphics\": ";
        File.WriteAllText(SettingsPath, Text);
        GlobalConfiguration config = CreateConfiguration();
        // A folder this process cannot write to keeps the copy from being made, as a full disk would.
        File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            try
            {
                File.WriteAllText(Path.Combine(_directory, "probe"), string.Empty);
                Assert.Ignore("This process can write to a folder it has no permission to write to.");
            }
            catch (UnauthorizedAccessException)
            {
            }

            config.Restore(SettingsPath);
        }
        finally
        {
            File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        // The folder can be written to again, so only the failed copy keeps these saves from replacing the file.
        config.GraphicsConfig.SelectedGpuName = "after";
        config.Save(SettingsPath);

        Assert.Multiple(() =>
        {
            Assert.That(config.RestoreFileFailure, Is.Not.Null);
            Assert.That(config.RestoreBackupPath, Is.Null);
            Assert.That(UnreadableCopies(), Is.Empty);
            Assert.That(File.ReadAllText(SettingsPath), Is.EqualTo(Text));
        });
    }

    [Test]
    public void A_missing_file_is_not_a_failure_and_nothing_is_copied()
    {
        GlobalConfiguration config = CreateConfiguration();
        config.Restore(SettingsPath);

        Assert.Multiple(() =>
        {
            Assert.That(config.RestoreFileFailure, Is.Null);
            Assert.That(config.RestoreBackupPath, Is.Null);
            Assert.That(Directory.GetFileSystemEntries(_directory), Is.Empty);
        });
    }
}
