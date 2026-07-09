using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
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
        Scene scene = CreateSceneWithElement(out Element element);
        var rect = new RectShape
        {
            Name = "hero mark",
            Width = { CurrentValue = 200 },
            Height = { CurrentValue = 120 },
            Opacity = { Animation = CreateLinearFloatAnimation(0, 100) }
        };
        element.AddObject(rect);

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

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
        Scene scene = CreateSceneWithElement(out Element element);
        var rect = new RectShape
        {
            Name = "fading rect",
            Width = { CurrentValue = 200 },
            Height = { CurrentValue = 120 },
            Opacity = { Animation = CreateLinearFloatAnimation(0, 100) }
        };
        element.AddObject(rect);

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

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
                            [nameof(KeyFrameAnimation.KeyFrames)] = new JsonArray(
                                new JsonObject { ["$replace"] = true },
                                CreateKeyFrameDocument(TimeSpan.FromSeconds(0.5), 25),
                                CreateKeyFrameDocument(TimeSpan.FromSeconds(2), 90),
                                CreateKeyFrameDocument(TimeSpan.FromSeconds(3), 40))
                        }
                    }
                })
            })
        };

        ToolResult<ApplyEditResponse> applied = tools.ApplyEdit(
            patch: replacePatch,
            schemaVersion: SchemaVersion.Current);
        Assert.That(applied.IsSuccess, Is.True, applied.Error?.Message);

        var animation = (KeyFrameAnimation<float>)rect.Opacity.Animation!;
        KeyFrame<float>[] frames = animation.KeyFrames.Cast<KeyFrame<float>>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(frames, Has.Length.EqualTo(3));
            Assert.That(frames.Select(frame => frame.KeyTime), Is.EqualTo(new[]
            {
                TimeSpan.FromSeconds(0.5),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(3)
            }));
            Assert.That(frames.Select(frame => frame.Value), Is.EqualTo(new[] { 25f, 90f, 40f }));
        });
    }

    private static JsonObject CreateKeyFrameDocument(TimeSpan keyTime, float value)
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

    private static KeyFrameAnimation<float> CreateLinearFloatAnimation(float firstValue, float secondValue)
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(
            new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = firstValue, Easing = new LinearEasing() },
            out _);
        animation.KeyFrames.Add(
            new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1), Value = secondValue, Easing = new LinearEasing() },
            out _);
        return animation;
    }

    private static Scene CreateSceneWithElement(out Element element)
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scene = new Scene(1920, 1080, "Scene")
        {
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(4),
            Uri = new Uri(Path.Combine(dir, "element.belm"))
        };
        scene.Children.Add(element);
        return scene;
    }
}
