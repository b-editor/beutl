using System;
using System.IO;
using System.Text.Json.Nodes;
using Beutl.Configuration;

namespace Beutl.UnitTests.Configuration;

public class GlobalConfigurationTests
{
    private static string NewSettingsFile()
    {
        return Path.Combine(ArtifactProvider.GetArtifactDirectory(), "settings.json");
    }

    private static bool? ReadBackupSetting(string file)
    {
        if (JsonHelper.JsonRestore(file) is JsonObject obj && obj["Backup"] is JsonObject backup)
        {
            return backup.TryGetPropertyValueAsJsonValue("BackupSettings", out bool value) ? value : null;
        }
        return null;
    }

    private static bool? ReadEditorSwapTimeline(string file)
    {
        if (JsonHelper.JsonRestore(file) is JsonObject obj && obj["Editor"] is JsonObject editor)
        {
            return editor.TryGetPropertyValueAsJsonValue("SwapTimelineScrollDirection", out bool value) ? value : null;
        }
        return null;
    }

    [Test]
    public void Save_WritesExpectedEditorSection()
    {
        var gc = GlobalConfiguration.Instance;
        string file = NewSettingsFile();

        bool original = gc.BackupConfig.BackupSettings;
        bool originalEditor = gc.EditorConfig.SwapTimelineScrollDirection;
        try
        {
            gc.EditorConfig.SwapTimelineScrollDirection = true;
            gc.Save(file);

            bool? written = ReadEditorSwapTimeline(file);
            Assert.That(written, Is.True);
        }
        finally
        {
            gc.BackupConfig.BackupSettings = original;
            gc.EditorConfig.SwapTimelineScrollDirection = originalEditor;
        }
    }

    [Test]
    public void AutoSave_OnSubConfigChange_WritesFile()
    {
        var gc = GlobalConfiguration.Instance;
        string file = Path.Combine(ArtifactProvider.GetArtifactDirectory(), "autosave.settings.json");

        bool original = gc.EditorConfig.SwapTimelineScrollDirection;
        try
        {
            gc.Save(file);
            bool? before = ReadEditorSwapTimeline(file);
            bool newVal = !gc.EditorConfig.SwapTimelineScrollDirection;
            gc.EditorConfig.SwapTimelineScrollDirection = newVal;

            bool? after = ReadEditorSwapTimeline(file);
            Assert.That(after, Is.EqualTo(newVal));

            // Sanity: before may be null if first write, but after should be defined
            Assert.That(after.HasValue, Is.True);
        }
        finally
        {
            gc.EditorConfig.SwapTimelineScrollDirection = original;
        }
    }
}
