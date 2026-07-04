using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class DeclarativeAnimationTests
{
    [Test]
    public void Patch_creates_new_object_with_inline_keyframe_animation()
    {
        Scene scene = CreateSceneWithText(out Element element, out _);
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        JsonObject createPatch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                ["Objects"] = new JsonArray(new JsonObject
                {
                    ["$type"] = IdentityHelper.WriteDiscriminator(typeof(TextBlock)),
                    [nameof(CoreObject.Name)] = "animated-new-text",
                    [nameof(TextBlock.Text)] = "Animated",
                    ["Animations"] = new JsonObject
                    {
                        ["Opacity"] = CreateOpacityAnimationDocument()
                    }
                })
            })
        };

        ToolResult<ApplyEditResponse> createApply = tools.ApplyEdit(
            patch: createPatch,
            schemaVersion: SchemaVersion.Current);
        TextBlock createdText = element.Objects
            .OfType<TextBlock>()
            .Single(item => item.Name == "animated-new-text");
        var animation = (KeyFrameAnimation<float>)createdText.Opacity.Animation!;

        Assert.Multiple(() =>
        {
            Assert.That(createApply.IsSuccess, Is.True, createApply.Error?.Message);
            Assert.That(createdText.Text.CurrentValue, Is.EqualTo("Animated"));
            Assert.That(animation.KeyFrames, Has.Count.EqualTo(2));
            Assert.That(((KeyFrame<float>)animation.KeyFrames[1]).Value, Is.EqualTo(100f));
        });
    }

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

    [Test]
    public async Task Apply_edit_file_session_evaluates_relative_keyframes_against_element_local_time()
    {
        string workspace = CreateWorkspace();
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        var sessionTools = new SessionTools(new FileProjectSessionGateway(source, manager), manager, new WorkspaceGuard(workspace), new DestructiveGuard());
        var editTools = new EditTools(manager);
        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            "local-keyframes.bep",
            width: 320,
            height: 180,
            frameRate: 30,
            duration: "00:00:08");
        Assert.That(created.IsSuccess, Is.True, created.Error?.Message);

        JsonObject patch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["$type"] = IdentityHelper.WriteDiscriminator(typeof(Element)),
                [nameof(CoreObject.Name)] = "delayed-local-animation",
                [nameof(Element.Start)] = TimeSpan.FromSeconds(5).ToString("c"),
                [nameof(Element.Length)] = TimeSpan.FromSeconds(2).ToString("c"),
                [nameof(Element.Objects)] = new JsonArray(new JsonObject
                {
                    ["$type"] = IdentityHelper.WriteDiscriminator(typeof(RectShape)),
                    [nameof(CoreObject.Name)] = "fading-rect",
                    [nameof(RectShape.Width)] = 120,
                    [nameof(RectShape.Height)] = 80,
                    [nameof(Shape.Fill)] = CoreSerializer.SerializeToJsonObject(new SolidColorBrush(Colors.White)),
                    ["Animations"] = new JsonObject
                    {
                        [nameof(Drawable.Opacity)] = CreateLinearOpacityAnimationDocument(100, 0)
                    }
                })
            })
        };

        ToolResult<ApplyEditResponse> apply = editTools.ApplyEdit(
            patch: patch,
            schemaVersion: SchemaVersion.Current);
        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);

        Element element = source.CurrentFileSession!.Scene.Children.Single();
        var rect = (RectShape)element.Objects.Single();
        var animation = (KeyFrameAnimation<float>)rect.Opacity.Animation!;
        float actual = rect.Opacity.GetValue(new CompositionContext(TimeSpan.FromSeconds(5.5)));

        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(animation.UseGlobalClock, Is.False);
            Assert.That(animation.KeyFrames.Cast<KeyFrame<float>>().Select(frame => frame.KeyTime),
                Is.EqualTo(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1) }));
            Assert.That(actual, Is.EqualTo(50f).Within(0.001f));
        });
    }

    [Test]
    public async Task Apply_edit_file_session_still_render_evaluates_nested_relative_keyframes_against_element_local_time()
    {
        string workspace = CreateWorkspace();
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        var sessionTools = new SessionTools(new FileProjectSessionGateway(source, manager), manager, new WorkspaceGuard(workspace), new DestructiveGuard());
        var editTools = new EditTools(manager);
        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            "nested-local-keyframes.bep",
            width: 320,
            height: 180,
            frameRate: 30,
            duration: "00:00:05");
        Assert.That(created.IsSuccess, Is.True, created.Error?.Message);

        JsonObject patch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["$type"] = IdentityHelper.WriteDiscriminator(typeof(Element)),
                [nameof(CoreObject.Name)] = "delayed-nested-local-animation",
                [nameof(Element.Start)] = TimeSpan.FromSeconds(2).ToString("c"),
                [nameof(Element.Length)] = TimeSpan.FromSeconds(1.5).ToString("c"),
                [nameof(Element.Objects)] = new JsonArray(new JsonObject
                {
                    ["$type"] = IdentityHelper.WriteDiscriminator(typeof(RectShape)),
                    [nameof(CoreObject.Name)] = "moving-fading-rect",
                    [nameof(RectShape.Width)] = 120,
                    [nameof(RectShape.Height)] = 80,
                    [nameof(Shape.Fill)] = CoreSerializer.SerializeToJsonObject(new SolidColorBrush(Colors.White)),
                    [nameof(Drawable.Transform)] = CreateTransformGroupWithAnimatedTranslateYDocument(),
                    ["Animations"] = new JsonObject
                    {
                        [nameof(Drawable.Opacity)] = CreateLinearFloatAnimationDocument(0, 100)
                    }
                })
            })
        };

        ToolResult<ApplyEditResponse> apply = editTools.ApplyEdit(
            patch: patch,
            schemaVersion: SchemaVersion.Current);
        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);

        Scene scene = source.CurrentFileSession!.Scene;
        Element element = scene.Children.Single();
        var rect = (RectShape)element.Objects.Single();
        var transform = (TransformGroup)rect.Transform.CurrentValue!;
        var translate = (TranslateTransform)transform.Children.Single();
        string outputPath = Path.Combine(workspace, "nested-local-keyframes.png");
        var renderer = new StillRenderer();

        RenderStillResponse render = await renderer.RenderAsync(
            scene,
            TimeSpan.FromSeconds(2.5),
            outputPath,
            renderScale: 1,
            CancellationToken.None);
        float opacity = rect.Opacity.GetValue(new CompositionContext(TimeSpan.FromSeconds(2.5)));
        float translateY = translate.Y.GetValue(new CompositionContext(TimeSpan.FromSeconds(2.5)));

        Assert.Multiple(() =>
        {
            Assert.That(render.VisibilityAnalysis!.VisiblePixelRatio, Is.GreaterThan(0));
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(((KeyFrameAnimation<float>)rect.Opacity.Animation!).UseGlobalClock, Is.False);
            Assert.That(((KeyFrameAnimation<float>)translate.Y.Animation!).UseGlobalClock, Is.False);
            Assert.That(opacity, Is.EqualTo(50f).Within(0.001f));
            Assert.That(translateY, Is.EqualTo(50f).Within(0.001f));
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

    private static JsonObject CreateLinearOpacityAnimationDocument(float firstValue, float secondValue)
        => CreateLinearFloatAnimationDocument(firstValue, secondValue);

    private static JsonObject CreateLinearFloatAnimationDocument(float firstValue, float secondValue)
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(
            new KeyFrame<float>
            {
                KeyTime = TimeSpan.Zero,
                Value = firstValue,
                Easing = new LinearEasing()
            },
            out _);
        animation.KeyFrames.Add(
            new KeyFrame<float>
            {
                KeyTime = TimeSpan.FromSeconds(1),
                Value = secondValue,
                Easing = new LinearEasing()
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

    private static JsonObject CreateTransformGroupWithAnimatedTranslateYDocument()
    {
        var transform = new TransformGroup
        {
            Children =
            {
                new TranslateTransform
                {
                    Y =
                    {
                        Animation = CreateLinearFloatAnimation(0, 100)
                    }
                }
            }
        };

        JsonObject transformJson = CoreSerializer.SerializeToJsonObject(transform);
        RemoveIds(transformJson);
        return transformJson;
    }

    private static KeyFrameAnimation<float> CreateLinearFloatAnimation(float firstValue, float secondValue)
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(
            new KeyFrame<float>
            {
                KeyTime = TimeSpan.Zero,
                Value = firstValue,
                Easing = new LinearEasing()
            },
            out _);
        animation.KeyFrames.Add(
            new KeyFrame<float>
            {
                KeyTime = TimeSpan.FromSeconds(1),
                Value = secondValue,
                Easing = new LinearEasing()
            },
            out _);

        return animation;
    }

    private static void RemoveIds(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove(nameof(CoreObject.Id));
            foreach (JsonNode? child in obj.Select(pair => pair.Value).ToArray())
            {
                RemoveIds(child);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array.ToArray())
            {
                RemoveIds(child);
            }
        }
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

    private static string CreateWorkspace()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
