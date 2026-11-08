using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Audio;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

// Issue #4186: a member the document leaves out means the property's default, which is null only for
// a property that defaults to null. A SourceSound whose channel map became null could not be exported.
[TestFixture]
public sealed class AbsentPropertyDefaultTests
{
    [Test]
    public void New_sound_without_a_channel_map_uses_auto()
    {
        Scene scene = TestWorkspace.CreateScene();
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: InsertElementWith<SourceSound>(),
            schemaVersion: SchemaVersion.Current);

        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
        var sound = (SourceSound)scene.Children.Single().Objects.Single();
        Assert.That(sound.ChannelMap.Value, Is.EqualTo(AudioChannelMap.Auto));
    }

    [Test]
    public void New_text_without_a_font_family_uses_the_default_font()
    {
        Scene scene = TestWorkspace.CreateScene();
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: InsertElementWith<TextBlock>(),
            schemaVersion: SchemaVersion.Current);

        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
        var text = (TextBlock)scene.Children.Single().Objects.Single();
        Assert.That(text.FontFamily.Value, Is.EqualTo(FontFamily.Default));
    }

    [Test]
    public void Patching_a_channel_map_to_null_returns_it_to_auto()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.Zero, MediaTime.FromSeconds(1));
        var sound = new SourceSound();
        sound.ChannelMap.Value = new AudioChannelMap(AudioChannelFormat.Mono, AudioChannelLayout.Stereo, [0]);
        element.AddObject(sound);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = element.Id.Value.ToString(),
                    [nameof(Element.Objects)] = new JsonArray(new JsonObject
                    {
                        [nameof(CoreObject.Id)] = sound.Id.Value.ToString(),
                        [nameof(SourceSound.ChannelMap)] = null
                    })
                })
            },
            schemaVersion: SchemaVersion.Current);

        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
        Assert.That(sound.ChannelMap.Value, Is.EqualTo(AudioChannelMap.Auto));
    }

    private static JsonObject InsertElementWith<T>()
    {
        return new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["$type"] = IdentityHelper.WriteDiscriminator(typeof(Element)),
                [nameof(Element.Start)] = "00:00:00",
                [nameof(Element.Length)] = "00:00:01",
                [nameof(Element.Objects)] = new JsonArray(new JsonObject
                {
                    ["$type"] = IdentityHelper.WriteDiscriminator(typeof(T))
                })
            })
        };
    }
}
