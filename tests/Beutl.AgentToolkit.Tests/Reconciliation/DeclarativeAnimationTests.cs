using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class DeclarativeAnimationTests
{
    [Test]
    public void Patch_adds_updates_and_removes_keyframes_through_declarative_apply()
    {
        Scene scene = CreateSceneWithText(out Element element, out TextBlock text);
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject createPatch = PatchTextObject(
            element,
            text,
            new JsonObject
            {
                ["Animations"] = new JsonObject
                {
                    ["Opacity"] = CreateOpacityAnimationDocument()
                }
            });

        ToolResult<ApplyEditResponse> createApply = tools.ApplyEdit(
            patch: createPatch,
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(createApply.IsSuccess, Is.True, createApply.Error?.Message);
            Assert.That(text.Opacity.Animation, Is.Not.Null);
        });

        var createdAnimation = (KeyFrameAnimation<float>)text.Opacity.Animation!;
        KeyFrame<float>[] createdFrames = createdAnimation.KeyFrames.Cast<KeyFrame<float>>().ToArray();
        float[] createdValues = createdFrames.Select(frame => frame.Value).ToArray();

        JsonObject updatePatch = PatchTextObject(
            element,
            text,
            new JsonObject
            {
                ["Animations"] = new JsonObject
                {
                    ["Opacity"] = new JsonObject
                    {
                        ["KeyFrames"] = new JsonArray(
                            new JsonObject
                            {
                                [nameof(CoreObject.Id)] = createdFrames[0].Id.ToString(),
                                ["$delete"] = true
                            },
                            new JsonObject
                            {
                                [nameof(CoreObject.Id)] = createdFrames[1].Id.ToString(),
                                [nameof(KeyFrame.KeyTime)] = TimeSpan.FromSeconds(1.5).ToString("c"),
                                [nameof(KeyFrame<float>.Value)] = 80,
                                [nameof(KeyFrame.Easing)] = nameof(HoldEasing)
                            })
                    }
                }
            });

        ToolResult<ApplyEditResponse> updateApply = tools.ApplyEdit(
            patch: updatePatch,
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(updateApply.IsSuccess, Is.True, updateApply.Error?.Message);
        });

        var updatedAnimation = (KeyFrameAnimation<float>)text.Opacity.Animation!;
        var remaining = (KeyFrame<float>)updatedAnimation.KeyFrames.Single();

        Assert.Multiple(() =>
        {
            Assert.That(createApply.IsSuccess, Is.True);
            Assert.That(createApply.Value!.Changes, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(createdFrames, Has.Length.EqualTo(2));
            Assert.That(createdValues, Is.EqualTo(new[] { 0f, 100f }));
            Assert.That(updateApply.IsSuccess, Is.True);
            Assert.That(updateApply.Value!.Changes.Select(change => change.Operation), Does.Contain(ChangeOperations.RemoveChild));
            Assert.That(remaining.Id, Is.EqualTo(createdFrames[1].Id));
            Assert.That(remaining.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
            Assert.That(remaining.Value, Is.EqualTo(80f));
            Assert.That(remaining.Easing, Is.InstanceOf<HoldEasing>());
        });

        session.History.Undo();

        Assert.That(createdAnimation.KeyFrames, Has.Count.EqualTo(2));
    }

    [Test]
    public void Full_desired_round_trip_preserves_serialized_easing_type_strings()
    {
        Scene scene = CreateSceneWithText(out Element element, out TextBlock text);
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject createPatch = PatchTextObject(
            element,
            text,
            new JsonObject
            {
                ["Animations"] = new JsonObject
                {
                    ["Opacity"] = CreateOpacityAnimationDocument()
                }
            });

        ToolResult<ApplyEditResponse> createApply = tools.ApplyEdit(
            patch: createPatch,
            schemaVersion: SchemaVersion.Current);

        JsonObject desired = session.Documents.Read(session.Root);
        ToolResult<ApplyEditResponse> roundTripApply = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        var animation = (KeyFrameAnimation<float>)text.Opacity.Animation!;
        var second = (KeyFrame<float>)animation.KeyFrames[1];

        Assert.Multiple(() =>
        {
            Assert.That(createApply.IsSuccess, Is.True, createApply.Error?.Message);
            Assert.That(roundTripApply.IsSuccess, Is.True, roundTripApply.Error?.Message);
            Assert.That(second.Easing, Is.InstanceOf<SineEaseOut>());
        });
    }

    [Test]
    public void Apply_edit_warns_but_applies_relative_keyframes_outside_element_length()
    {
        Scene scene = CreateSceneWithText(out Element element, out TextBlock text);
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject createPatch = PatchTextObject(
            element,
            text,
            new JsonObject
            {
                ["Animations"] = new JsonObject
                {
                    ["Opacity"] = CreateOpacityAnimationDocument(TimeSpan.Zero, TimeSpan.FromSeconds(5))
                }
            });

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: createPatch,
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(apply.Value!.Valid, Is.True);
            Assert.That(apply.Value.Validation.Select(item => item.Status), Does.Contain(ValidationStatus.Warning));
            Assert.That(apply.Value.Validation.Single(item => item.Status == ValidationStatus.Warning).Message, Does.Contain("UseGlobalClock=false"));
            Assert.That(apply.Value.Validation.Single(item => item.Status == ValidationStatus.Warning).Hint, Does.Contain("UseGlobalClock=true"));
            Assert.That(text.Opacity.Animation, Is.Not.Null);
        });
    }

    [Test]
    public void Apply_edit_warns_when_element_length_change_makes_relative_keyframes_outside_range()
    {
        Scene scene = CreateSceneWithText(out Element element, out TextBlock text);
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject createPatch = PatchTextObject(
            element,
            text,
            new JsonObject
            {
                ["Animations"] = new JsonObject
                {
                    ["Opacity"] = CreateOpacityAnimationDocument(TimeSpan.Zero, TimeSpan.FromSeconds(3.5))
                }
            });
        ToolResult<ApplyEditResponse> createApply = tools.ApplyEdit(
            patch: createPatch,
            schemaVersion: SchemaVersion.Current);

        JsonObject retimePatch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                [nameof(Element.Length)] = TimeSpan.FromSeconds(2).ToString("c")
            })
        };
        ToolResult<ApplyEditResponse> retimeApply = tools.ApplyEdit(
            patch: retimePatch,
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(createApply.IsSuccess, Is.True, createApply.Error?.Message);
            Assert.That(createApply.Value!.Validation.Select(item => item.Status), Does.Not.Contain(ValidationStatus.Warning));
            Assert.That(retimeApply.IsSuccess, Is.True, retimeApply.Error?.Message);
            Assert.That(retimeApply.Value!.Valid, Is.True);
            Assert.That(retimeApply.Value.Validation.Select(item => item.Status), Does.Contain(ValidationStatus.Warning));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    private static JsonObject PatchTextObject(Element element, TextBlock text, JsonObject textPatch)
    {
        textPatch[nameof(CoreObject.Id)] = text.Id.ToString();
        return new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                ["Objects"] = new JsonArray(textPatch)
            })
        };
    }

    private static JsonObject CreateOpacityAnimationDocument()
        => CreateOpacityAnimationDocument(TimeSpan.Zero, TimeSpan.FromSeconds(1));

    private static JsonObject CreateOpacityAnimationDocument(TimeSpan firstKeyTime, TimeSpan secondKeyTime)
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(
            new KeyFrame<float>
            {
                KeyTime = firstKeyTime,
                Value = 0,
                Easing = new LinearEasing()
            },
            out _);
        animation.KeyFrames.Add(
            new KeyFrame<float>
            {
                KeyTime = secondKeyTime,
                Value = 100,
                Easing = new SineEaseOut()
            },
            out _);

        JsonObject animationJson = CoreSerializer.SerializeToJsonObject(animation);
        animationJson.Remove(nameof(CoreObject.Id));

        foreach (JsonObject keyframe in ((JsonArray)animationJson[nameof(KeyFrameAnimation.KeyFrames)]!).OfType<JsonObject>())
        {
            keyframe.Remove(nameof(CoreObject.Id));
        }

        return animationJson;
    }

    private static Scene CreateSceneWithText(out Element element, out TextBlock text)
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
        text = new TextBlock { Text = { CurrentValue = "Launch" } };
        element.AddObject(text);
        scene.Children.Add(element);
        return scene;
    }
}
