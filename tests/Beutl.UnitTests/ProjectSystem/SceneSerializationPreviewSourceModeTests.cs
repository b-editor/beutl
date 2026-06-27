using Beutl.Media.Proxy;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class SceneSerializationPreviewSourceModeTests
{
    private static readonly CoreSerializerOptions s_options = new()
    {
        Mode = CoreSerializationMode.Write | CoreSerializationMode.EmbedReferencedObjects,
    };

    [Test]
    public void NewScene_DefaultsToPreferProxy()
    {
        var scene = new Scene();

        Assert.That(scene.PreviewSourceMode, Is.EqualTo(PreviewSourceMode.PreferProxy));
    }

    [Test]
    public void JsonRoundTrip_PreservesPreviewSourceMode()
    {
        var scene = new Scene
        {
            PreviewSourceMode = PreviewSourceMode.ForceOriginal,
        };

        var json = CoreSerializer.SerializeToJsonObject(scene, s_options);
        var restored = (Scene)CoreSerializer.DeserializeFromJsonObject(json, typeof(Scene), s_options);

        Assert.That(restored.PreviewSourceMode, Is.EqualTo(PreviewSourceMode.ForceOriginal));
    }

    [Test]
    public void LegacyJsonWithoutPreviewSourceMode_DefaultsToPreferProxy()
    {
        var scene = new Scene
        {
            PreviewSourceMode = PreviewSourceMode.ForceOriginal,
        };
        var json = CoreSerializer.SerializeToJsonObject(scene, s_options);
        json.Remove(nameof(Scene.PreviewSourceMode));

        var restored = (Scene)CoreSerializer.DeserializeFromJsonObject(json, typeof(Scene), s_options);

        Assert.That(restored.PreviewSourceMode, Is.EqualTo(PreviewSourceMode.PreferProxy));
    }
}
