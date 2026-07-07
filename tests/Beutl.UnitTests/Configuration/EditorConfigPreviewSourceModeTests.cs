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
