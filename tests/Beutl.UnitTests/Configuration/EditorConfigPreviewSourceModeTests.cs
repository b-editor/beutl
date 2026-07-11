using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Serialization;

namespace Beutl.UnitTests.Configuration;

[TestFixture]
public class EditorConfigPreviewSourceModeTests
{
    [Test]
    public void DefaultValue_IsPreferProxy()
    {
        var config = new EditorConfig();

        Assert.That(config.PreviewSourceMode, Is.EqualTo(PreviewSourceMode.PreferProxy));
    }

    [Test]
    public void SetValue_RaisesConfigurationChanged()
    {
        var config = new EditorConfig();
        bool raised = false;
        config.ConfigurationChanged += (_, _) => raised = true;

        config.PreviewSourceMode = PreviewSourceMode.ForceOriginal;

        Assert.Multiple(() =>
        {
            Assert.That(config.PreviewSourceMode, Is.EqualTo(PreviewSourceMode.ForceOriginal));
            Assert.That(raised, Is.True);
        });
    }

    [TestCase(0, PreviewSourceMode.PreferProxy)]
    [TestCase(1, PreviewSourceMode.ForceOriginal)]
    public void SetPreviewSourceModeFromIndex_ValidIndex_UpdatesValue(int index, PreviewSourceMode expected)
    {
        var config = new EditorConfig { PreviewSourceMode = PreviewSourceMode.ForceOriginal };

        config.SetPreviewSourceModeFromIndex(index);

        Assert.That(config.PreviewSourceMode, Is.EqualTo(expected));
    }

    // A ComboBox with no selection reports SelectedIndex -1; an out-of-range index must be ignored so an
    // undefined enum (which never equals PreferProxy) is never persisted.
    [TestCase(-1)]
    [TestCase(2)]
    public void SetPreviewSourceModeFromIndex_OutOfRange_LeavesValueUnchanged(int index)
    {
        var config = new EditorConfig { PreviewSourceMode = PreviewSourceMode.ForceOriginal };

        config.SetPreviewSourceModeFromIndex(index);

        Assert.That(config.PreviewSourceMode, Is.EqualTo(PreviewSourceMode.ForceOriginal));
    }

    [Test]
    public void JsonRoundTrip_PreservesPreviewSourceMode()
    {
        var config = new EditorConfig { PreviewSourceMode = PreviewSourceMode.ForceOriginal };

        JsonObject json = CoreSerializer.SerializeToJsonObject(config);
        var restored = new EditorConfig();
        CoreSerializer.PopulateFromJsonObject(restored, json);

        Assert.That(restored.PreviewSourceMode, Is.EqualTo(PreviewSourceMode.ForceOriginal));
    }

    [Test]
    public void LegacyJsonWithoutPreviewSourceMode_DefaultsToPreferProxy()
    {
        var config = new EditorConfig { PreviewSourceMode = PreviewSourceMode.ForceOriginal };
        JsonObject json = CoreSerializer.SerializeToJsonObject(config);
        json.Remove(nameof(EditorConfig.PreviewSourceMode));

        var restored = new EditorConfig();
        CoreSerializer.PopulateFromJsonObject(restored, json);

        Assert.That(restored.PreviewSourceMode, Is.EqualTo(PreviewSourceMode.PreferProxy));
    }
}
