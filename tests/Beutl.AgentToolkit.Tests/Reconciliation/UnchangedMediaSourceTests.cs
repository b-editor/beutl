using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Audio;
using Beutl.Editor.Operations;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

// Issue #4191: apply_edit writes the whole document back, and a media source is saved as a plain file reference, so
// populating it made a new object on every edit. That recorded a change nothing asked for, on every media element.
[TestFixture]
public sealed class UnchangedMediaSourceTests
{
    [Test]
    public void An_edit_elsewhere_leaves_the_media_source_alone()
    {
        Scene scene = TestWorkspace.CreateScene();
        SourceSound sound = AddSound(scene, "beat.wav");
        SoundSource source = sound.Source.Value!;
        Element other = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(2), MediaTime.FromSeconds(1));
        var rect = new RectShape();
        other.AddObject(rect);
        using var session = new AgentToolkitTestSession(scene);

        ToolResult<ApplyEditResponse> apply = session.CreateEditTools().ApplyEdit(
            patch: PatchObject(other, new JsonObject
            {
                [nameof(CoreObject.Id)] = rect.Id.Value.ToString(),
                [nameof(RectShape.Width)] = 200
            }),
            schemaVersion: SchemaVersion.Current);

        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(rect.Width.Value, Is.EqualTo(200));
            Assert.That(sound.Source.Value, Is.SameAs(source));
            Assert.That(
                session.History.PeekUndo()!.Operations.OfType<IUpdatePropertyValueOperation>()
                    .Where(operation => ReferenceEquals(operation.Object, sound)),
                Is.Empty);
        });
    }

    [Test]
    public void Pointing_the_source_at_another_file_replaces_it()
    {
        Scene scene = TestWorkspace.CreateScene();
        SourceSound sound = AddSound(scene, "beat.wav");
        var other = new Uri(Path.Combine(Path.GetTempPath(), "other.wav"));
        using var session = new AgentToolkitTestSession(scene);

        ToolResult<ApplyEditResponse> apply = session.CreateEditTools().ApplyEdit(
            patch: PatchObject(scene.Children.Single(), new JsonObject
            {
                [nameof(CoreObject.Id)] = sound.Id.Value.ToString(),
                [nameof(SourceSound.Source)] = other.AbsoluteUri
            }),
            schemaVersion: SchemaVersion.Current);

        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
        Assert.That(sound.Source.Value!.Uri, Is.EqualTo(other));
    }

    private static SourceSound AddSound(Scene scene, string fileName)
    {
        Element element = TestWorkspace.AddElement(scene, MediaTime.Zero, MediaTime.FromSeconds(1));
        var sound = new SourceSound { Source = { Value = SoundSource.Open(Path.Combine(Path.GetTempPath(), fileName)) } };
        element.AddObject(sound);
        return sound;
    }

    private static JsonObject PatchObject(Element element, JsonObject patch)
    {
        return new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.Value.ToString(),
                [nameof(Element.Objects)] = new JsonArray(patch)
            })
        };
    }
}
