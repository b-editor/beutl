using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class AnimatedElementFieldReproTests
{
    [Test]
    public void Rename_element_succeeds_after_wrap_in_group_duplicate_of_animated_drawable()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.Zero, MediaTime.FromSeconds(4));
        var rect = new RectShape
        {
            Name = "hero mark",
            Width = { Value = 200 },
            Height = { Value = 120 },
        };
        rect.Opacity.SetKeyFrameAnimation(TestWorkspace.CreateLinearFloatAnimation(0, 100));
        element.AddObject(rect);

        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<DuplicateObjectResponse> duplicated =
            tools.DuplicateObject(rect.Id.ToString(), wrapInGroup: true);
        Assert.That(duplicated.IsSuccess, Is.True, duplicated.Error?.Message);

        JsonObject renamePatch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                [nameof(CoreObject.Name)] = "[role:decorative] hero mark bloom"
            })
        };

        ToolResult<ApplyEditResponse> renamed = tools.ApplyEdit(
            patch: renamePatch,
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(renamed.IsSuccess, Is.True, renamed.Error?.Message);
            Assert.That(element.Name, Is.EqualTo("[role:decorative] hero mark bloom"));
        });
    }

    [Test]
    public void Replace_sentinel_rebuilds_nested_keyframe_array()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.Zero, MediaTime.FromSeconds(4));
        var rect = new RectShape
        {
            Name = "fading rect",
            Width = { Value = 200 },
            Height = { Value = 120 },
        };
        rect.Opacity.SetKeyFrameAnimation(TestWorkspace.CreateLinearFloatAnimation(0, 100));
        element.AddObject(rect);

        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        JsonObject replacePatch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                ["Objects"] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = rect.Id.ToString(),
                    ["Animations"] = new JsonObject
                    {
                        [nameof(Drawable.Opacity)] = new JsonObject
                        {
                            [nameof(IKeyFrameAnimation.KeyFrames)] = new JsonArray(
                                new JsonObject { ["$replace"] = true },
                                CreateKeyFrameDocument(MediaTime.FromSeconds(0.5), 25),
                                CreateKeyFrameDocument(MediaTime.FromSeconds(2), 90),
                                CreateKeyFrameDocument(MediaTime.FromSeconds(3), 40))
                        }
                    }
                })
            })
        };

        ToolResult<ApplyEditResponse> applied = tools.ApplyEdit(
            patch: replacePatch,
            schemaVersion: SchemaVersion.Current);
        Assert.That(applied.IsSuccess, Is.True, applied.Error?.Message);

        var animation = (KeyFrameAnimation<float>)rect.Opacity.GetKeyFrameAnimation()!;
        KeyFrame<float>[] frames = animation.KeyFrames.Cast<KeyFrame<float>>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(frames, Has.Length.EqualTo(3));
            Assert.That(frames.Select(frame => frame.KeyTime), Is.EqualTo(new[]
            {
                MediaTime.FromSeconds(0.5),
                MediaTime.FromSeconds(2),
                MediaTime.FromSeconds(3)
            }));
            Assert.That(frames.Select(frame => frame.Value), Is.EqualTo(new[] { 25f, 90f, 40f }));
        });
    }

    private static JsonObject CreateKeyFrameDocument(MediaTime keyTime, float value)
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(new KeyFrame<float>
        {
            KeyTime = keyTime,
            Value = value,
            Easing = new LinearEasing()
        });
        json.Remove(nameof(CoreObject.Id));
        return json;
    }
}
