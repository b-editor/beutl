using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Schema;
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
    public void Patch_round_trips_and_edits_separated_color_tracks()
    {
        Scene scene = CreateSceneWithText(out Element element, out TextBlock text);
        var brush = new SolidColorBrush(Color.FromArgb(255, 20, 40, 60));
        text.Fill.Value = brush;
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        var desiredBrush = new SolidColorBrush(brush.Color.Value);
        desiredBrush.Id.Value = brush.Id.Value;
        var components = ComponentAnimations.Separate(desiredBrush.Color);
        var red = (IProperty<byte>)components.Components[0];
        var animation = new KeyFrameAnimation<byte>();
        animation.KeyFrames.Add(new KeyFrame<byte> { Value = { Value = 0 } });
        animation.KeyFrames.Add(new KeyFrame<byte> { Value = { Value = 255 }, KeyTime = { Value = MediaTime.FromSeconds(2) } });
        red.SetKeyFrameAnimation(animation);
        JsonObject brushJson = CoreSerializer.SerializeToJsonObject(desiredBrush);
        var newTracks = brushJson["Animations"]!["Color"]!.AsObject();
        newTracks.Remove("Id");
        var redJson = newTracks["Animations"]!["R"]!.AsObject();
        redJson.Remove("Id");
        foreach (var key in redJson["KeyFrames"]!.AsArray()) key!.AsObject().Remove("Id");
        var result = tools.ApplyEdit(patch: PatchTextObject(element, text, new JsonObject { ["Fill"] = brushJson }), schemaVersion: SchemaVersion.Current);
        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(brush.Color.GetPropertyAnimation(), Is.InstanceOf<IComponentAnimation>());
        Assert.That(brush.Color.Evaluate(new CompositionContext(MediaTime.FromSeconds(1))).R, Is.EqualTo(188));

        var liveComponents = (IComponentAnimation)brush.Color.GetPropertyAnimation()!;
        var liveRed = liveComponents.Components.Single(p => p.Name == "R").GetKeyFrameAnimation()!;
        var firstKey = liveRed.KeyFrames[0];
        brushJson = CoreSerializer.SerializeToJsonObject(brush);
        brushJson["Animations"]!["Color"]!["Animations"]!["R"]!["KeyFrames"]![0]!["KeyTime"] = TimeSpan.FromSeconds(3).ToString("c");
        brushJson["Animations"]!["Color"]!["Animations"]!["R"]!["KeyFrames"]![1]!["Value"] = 128;
        result = tools.ApplyEdit(patch: PatchTextObject(element, text, new JsonObject { ["Fill"] = brushJson }), schemaVersion: SchemaVersion.Current);
        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(brush.Color.Evaluate(new CompositionContext(MediaTime.FromSeconds(2))).R, Is.EqualTo(128));
        Assert.That(brush.Color.Evaluate(new CompositionContext(MediaTime.FromSeconds(2))).G, Is.EqualTo(40));
        Assert.Multiple(() =>
        {
            Assert.That(liveComponents.Components[0].GetKeyFrameAnimation(), Is.SameAs(liveRed));
            Assert.That(liveRed.KeyFrames.Last(), Is.SameAs(firstKey));
            Assert.That(brush.Color.Evaluate(new CompositionContext(MediaTime.FromSeconds(3))).R, Is.Zero);
        });
    }

    [Test]
    public void New_component_tracks_are_sorted_and_use_the_owner_bounds()
    {
        Scene scene = CreateSceneWithText(out Element element, out TextBlock text);
        var blur = new Beutl.Graphics.Effects.Blur();
        text.FilterEffect.Value = blur;
        using var session = new AgentToolkitTestSession(scene);
        var desired = new Beutl.Graphics.Effects.Blur();
        var split = ComponentAnimations.Separate(desired.Sigma);
        var width = (IProperty<float>)split.Components.Single(p => p.Name == "Width");
        width.SetKeyFrameAnimation(TestWorkspace.CreateLinearFloatAnimation(10, 20));
        JsonObject json = SamplePatchBuilder.SerializeWithoutIds(desired);
        json["Id"] = blur.Id.Value.ToString();
        var keys = json["Animations"]!["Sigma"]!["Animations"]!["Width"]!["KeyFrames"]!.AsArray();
        keys[0]!["Value"] = -10;
        keys[0]!["KeyTime"] = TimeSpan.FromSeconds(3).ToString("c");

        var result = session.CreateEditTools().ApplyEdit(
            patch: PatchTextObject(element, text, new JsonObject { ["FilterEffect"] = json }), schemaVersion: SchemaVersion.Current);
        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var components = (IComponentAnimation)blur.Sigma.GetPropertyAnimation()!;
        var animation = components.Components.Single(p => p.Name == "Width").GetKeyFrameAnimation()!;
        Assert.That(animation.KeyFrames.Select(k => k.KeyTime.Value), Is.Ordered);
        Assert.That(animation.KeyFrames.Last().Value, Is.EqualTo(0f));
        Assert.That(blur.Sigma.Evaluate(new CompositionContext(MediaTime.FromSeconds(3))), Is.EqualTo(Size.Empty));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Removing_a_component_unit_source_preserves_tracks_and_supports_undo(bool usePatch)
    {
        Scene scene = CreateSceneWithText(out Element element, out TextBlock text);
        text.TransformOrigin.Value = new RelativePoint(10, 20, RelativeUnit.Absolute);
        var animation = new KeyFrameAnimation<RelativePoint>();
        animation.KeyFrames.Add(new KeyFrame<RelativePoint> { Value = { Value = new RelativePoint(0.25f, 0.5f, RelativeUnit.Relative) } });
        animation.KeyFrames.Add(new KeyFrame<RelativePoint>
        {
            KeyTime = { Value = MediaTime.FromSeconds(2) },
            Value = { Value = new RelativePoint(0.75f, 1, RelativeUnit.Relative) }
        });
        text.TransformOrigin.SetKeyFrameAnimation(animation);
        var components = (ComponentAnimation<RelativePoint, float>)ComponentAnimations.Separate(text.TransformOrigin);
        var unitSource = components.UnitSource.Value;
        var xAnimation = components.Components[0].GetKeyFrameAnimation()!;
        var firstKey = xAnimation.KeyFrames[0];
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        // Omitting UnitSource from a partial patch must retain the inherited schedule.
        var update = tools.ApplyEdit(patch: PatchAnimation(new JsonObject { ["BaseValue"] = "100, 200" }), schemaVersion: SchemaVersion.Current);
        Assert.That(update.IsSuccess, Is.True, update.Error?.Message);
        Assert.That(components.UnitSource.Value, Is.SameAs(unitSource));
        Assert.That(text.TransformOrigin.Evaluate(new CompositionContext(MediaTime.Zero)),
            Is.EqualTo(new RelativePoint(0.25f, 0.5f, RelativeUnit.Relative)));

        ToolResult<ApplyEditResponse> remove;
        if (usePatch)
        {
            remove = tools.ApplyEdit(patch: PatchAnimation(new JsonObject { ["UnitSource"] = null }), schemaVersion: SchemaVersion.Current);
        }
        else
        {
            JsonObject desired = session.Documents.Read(session.Root);
            desired["Elements"]![0]!["Objects"]![0]!["Animations"]![nameof(Drawable.TransformOrigin)]!.AsObject().Remove("UnitSource");
            remove = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);
        }

        Assert.That(remove.IsSuccess, Is.True, remove.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(components.UnitSource.Value, Is.Null);
            Assert.That(text.TransformOrigin.GetPropertyAnimation(), Is.SameAs(components));
            Assert.That(components.Components[0].GetKeyFrameAnimation(), Is.SameAs(xAnimation));
            Assert.That(xAnimation.KeyFrames[0], Is.SameAs(firstKey));
            Assert.That(text.TransformOrigin.Evaluate(new CompositionContext(MediaTime.Zero)),
                Is.EqualTo(new RelativePoint(0.25f, 0.5f, RelativeUnit.Absolute)));
        });
        session.History.Undo();
        Assert.That(components.UnitSource.Value, Is.SameAs(unitSource));
        Assert.That(text.TransformOrigin.Evaluate(new CompositionContext(MediaTime.Zero)).Unit, Is.EqualTo(RelativeUnit.Relative));
        session.History.Redo();
        Assert.That(components.UnitSource.Value, Is.Null);
        Assert.That(text.TransformOrigin.Evaluate(new CompositionContext(MediaTime.Zero)).Unit, Is.EqualTo(RelativeUnit.Absolute));

        JsonObject PatchAnimation(JsonObject patch) => PatchTextObject(element, text, new JsonObject
        {
            ["Animations"] = new JsonObject { [nameof(Drawable.TransformOrigin)] = patch }
        });
    }

    [Test]
    public void Patch_creates_new_object_with_inline_keyframe_animation()
    {
        Scene scene = CreateSceneWithText(out Element element, out _);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        JsonObject createPatch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.Value.ToString(),
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
            .Single(item => item.Name.Value == "animated-new-text");
        var animation = (KeyFrameAnimation<float>)createdText.Opacity.GetKeyFrameAnimation()!;

        Assert.Multiple(() =>
        {
            Assert.That(createApply.IsSuccess, Is.True, createApply.Error?.Message);
            Assert.That(createdText.Text.Value, Is.EqualTo("Animated"));
            Assert.That(animation.KeyFrames, Has.Count.EqualTo(2));
            Assert.That(((KeyFrame<float>)animation.KeyFrames[1]).Value.Value, Is.EqualTo(100f));
        });
    }

    [Test]
    public void Patch_adds_updates_and_removes_keyframes_through_declarative_apply()
    {
        Scene scene = CreateSceneWithText(out Element element, out TextBlock text);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

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
            Assert.That(text.Opacity.GetKeyFrameAnimation(), Is.Not.Null);
        });

        var createdAnimation = (KeyFrameAnimation<float>)text.Opacity.GetKeyFrameAnimation()!;
        KeyFrame<float>[] createdFrames = createdAnimation.KeyFrames.Cast<KeyFrame<float>>().ToArray();
        float[] createdValues = createdFrames.Select(frame => frame.Value.Value).ToArray();

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
                                [nameof(CoreObject.Id)] = createdFrames[0].Id.Value.ToString(),
                                ["$delete"] = true
                            },
                            new JsonObject
                            {
                                [nameof(CoreObject.Id)] = createdFrames[1].Id.Value.ToString(),
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

        Assert.That(updateApply.IsSuccess, Is.True, updateApply.Error?.Message);

        var updatedAnimation = (KeyFrameAnimation<float>)text.Opacity.GetKeyFrameAnimation()!;
        var remaining = (KeyFrame<float>)updatedAnimation.KeyFrames.Single();

        Assert.Multiple(() =>
        {
            Assert.That(createApply.Value!.Changes, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(createdFrames, Has.Length.EqualTo(2));
            Assert.That(createdValues, Is.EqualTo(new[] { 0f, 100f }));
            Assert.That(updateApply.Value!.Changes!.Select(change => change.Operation), Does.Contain(ChangeOperations.RemoveChild));
            Assert.That(remaining.Id.Value, Is.EqualTo(createdFrames[1].Id.Value));
            Assert.That(remaining.KeyTime.Value, Is.EqualTo(MediaTime.FromSeconds(1.5)));
            Assert.That(remaining.Value.Value, Is.EqualTo(80f));
            Assert.That(remaining.Easing.Value, Is.InstanceOf<HoldEasing>());
        });

        session.History.Undo();

        Assert.That(createdAnimation.KeyFrames, Has.Count.EqualTo(2));
    }

    [Test]
    public void Creating_an_animation_with_unsorted_keyframes_sorts_them_by_key_time()
    {
        Scene scene = CreateSceneWithText(out Element element, out TextBlock text);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        JsonObject animationDocument = CreateOpacityAnimationDocument(MediaTime.Zero, MediaTime.FromSeconds(1));
        var frames = (JsonArray)animationDocument[nameof(IKeyFrameAnimation.KeyFrames)]!;
        // Present the keyframes in descending KeyTime order; CoreSerializer preserves array order.
        animationDocument[nameof(IKeyFrameAnimation.KeyFrames)] = new JsonArray(
            frames[1]!.DeepClone(),
            frames[0]!.DeepClone());

        JsonObject createPatch = PatchTextObject(
            element,
            text,
            new JsonObject
            {
                ["Animations"] = new JsonObject
                {
                    ["Opacity"] = animationDocument
                }
            });

        ToolResult<ApplyEditResponse> createApply = tools.ApplyEdit(
            patch: createPatch,
            schemaVersion: SchemaVersion.Current);
        Assert.That(createApply.IsSuccess, Is.True, createApply.Error?.Message);

        var animation = (KeyFrameAnimation<float>)text.Opacity.GetKeyFrameAnimation()!;
        KeyFrame<float>[] resultFrames = animation.KeyFrames.Cast<KeyFrame<float>>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(resultFrames.Select(frame => frame.KeyTime.Value),
                Is.EqualTo(new[] { MediaTime.Zero, MediaTime.FromSeconds(1) }));
            Assert.That(resultFrames[0].Value.Value, Is.EqualTo(0f));
            Assert.That(resultFrames[1].Value.Value, Is.EqualTo(100f));
        });
    }

    [Test]
    public void Unknown_easing_type_is_rejected_instead_of_silently_falling_back_to_linear()
    {
        Scene scene = CreateSceneWithText(out Element element, out TextBlock text);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

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
        Assert.That(createApply.IsSuccess, Is.True, createApply.Error?.Message);

        var createdAnimation = (KeyFrameAnimation<float>)text.Opacity.GetKeyFrameAnimation()!;
        KeyFrame<float>[] createdFrames = createdAnimation.KeyFrames.Cast<KeyFrame<float>>().ToArray();
        Easing originalEasing = createdFrames[0].Easing.Value;

        JsonObject badPatch = PatchTextObject(
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
                                [nameof(CoreObject.Id)] = createdFrames[0].Id.Value.ToString(),
                                [nameof(KeyFrame.Easing)] = "EaseInOutCubicTYPO"
                            })
                    }
                }
            });

        ToolResult<ApplyEditResponse> badApply = tools.ApplyEdit(
            patch: badPatch,
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(badApply.IsSuccess, Is.False);
            Assert.That(badApply.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(createdFrames[0].Easing.Value, Is.SameAs(originalEasing));
        });
    }

    [Test]
    public void Full_desired_round_trip_preserves_serialized_easing_type_strings()
    {
        Scene scene = CreateSceneWithText(out Element element, out TextBlock text);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

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

        var animation = (KeyFrameAnimation<float>)text.Opacity.GetKeyFrameAnimation()!;
        var second = (KeyFrame<float>)animation.KeyFrames[1];

        Assert.Multiple(() =>
        {
            Assert.That(createApply.IsSuccess, Is.True, createApply.Error?.Message);
            Assert.That(roundTripApply.IsSuccess, Is.True, roundTripApply.Error?.Message);
            Assert.That(second.Easing.Value, Is.InstanceOf<SineEaseOut>());
        });
    }

    [Test]
    public void Apply_edit_warns_when_element_length_change_makes_relative_keyframes_outside_range()
    {
        Scene scene = CreateSceneWithText(out Element element, out TextBlock text);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        JsonObject createPatch = PatchTextObject(
            element,
            text,
            new JsonObject
            {
                ["Animations"] = new JsonObject
                {
                    ["Opacity"] = CreateOpacityAnimationDocument(MediaTime.Zero, MediaTime.FromSeconds(3.5))
                }
            });
        ToolResult<ApplyEditResponse> createApply = tools.ApplyEdit(
            patch: createPatch,
            schemaVersion: SchemaVersion.Current);

        JsonObject retimePatch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.Value.ToString(),
                [nameof(Element.Length)] = TimeSpan.FromSeconds(2).ToString("c")
            })
        };
        ToolResult<ApplyEditResponse> retimeApply = tools.ApplyEdit(
            patch: retimePatch,
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(createApply.IsSuccess, Is.True, createApply.Error?.Message);
            Assert.That(createApply.Value!.Validation!.Select(item => item.Status), Does.Not.Contain(ValidationStatus.Warning));
            Assert.That(retimeApply.IsSuccess, Is.True, retimeApply.Error?.Message);
            Assert.That(retimeApply.Value!.Valid, Is.True);
            Assert.That(retimeApply.Value.Validation!.Select(item => item.Status), Does.Contain(ValidationStatus.Warning));
            Assert.That(element.Length.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
        });
    }

    [Test]
    public async Task Apply_edit_file_session_evaluates_relative_keyframes_against_element_local_time()
    {
        string workspace = TestWorkspace.CreateDirectory();
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        var sessionTools = new SessionTools(new FileProjectSessionGateway(source, manager), manager, new DestructiveGuard(), new RenderJobManager());
        var editTools = new EditTools(manager);
        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            Path.Combine(workspace, "local-keyframes.bep"),
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
                        [nameof(Drawable.Opacity)] = CreateLinearFloatAnimationDocument(100, 0)
                    }
                })
            })
        };

        ToolResult<ApplyEditResponse> apply = editTools.ApplyEdit(
            patch: patch,
            schemaVersion: SchemaVersion.Current);
        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);

        Element element = ((FileEditingSession)source.CurrentSession!).Scene.Children.Single();
        var rect = (RectShape)element.Objects.Single();
        var animation = (KeyFrameAnimation<float>)rect.Opacity.GetKeyFrameAnimation()!;
        float actual = rect.Opacity.Evaluate(new CompositionContext(MediaTime.FromSeconds(5.5)));

        Assert.Multiple(() =>
        {
            Assert.That(element.Start.Value, Is.EqualTo(MediaTime.FromSeconds(5)));
            Assert.That(animation.UseGlobalClock.Value, Is.False);
            Assert.That(animation.KeyFrames.Cast<KeyFrame<float>>().Select(frame => frame.KeyTime.Value),
                Is.EqualTo(new[] { MediaTime.Zero, MediaTime.FromSeconds(1) }));
            Assert.That(actual, Is.EqualTo(50f).Within(0.001f));
        });
    }

    [Test]
    public async Task Apply_edit_file_session_still_render_evaluates_nested_relative_keyframes_against_element_local_time()
    {
        string workspace = TestWorkspace.CreateDirectory();
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        var sessionTools = new SessionTools(new FileProjectSessionGateway(source, manager), manager, new DestructiveGuard(), new RenderJobManager());
        var editTools = new EditTools(manager);
        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            Path.Combine(workspace, "nested-local-keyframes.bep"),
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

        Scene scene = ((FileEditingSession)source.CurrentSession!).Scene;
        Element element = scene.Children.Single();
        var rect = (RectShape)element.Objects.Single();
        var transform = (TransformGroup)rect.Transform.Value!;
        var translate = (TranslateTransform)transform.Children.Single();
        string outputPath = Path.Combine(workspace, "nested-local-keyframes.png");
        var renderer = new StillRenderer();

        RenderStillResponse render = await renderer.RenderAsync(
            scene,
            MediaTime.FromSeconds(2.5),
            outputPath,
            renderScale: 1,
            CancellationToken.None);
        float opacity = rect.Opacity.Evaluate(new CompositionContext(MediaTime.FromSeconds(2.5)));
        float translateY = translate.Y.Evaluate(new CompositionContext(MediaTime.FromSeconds(2.5)));

        Assert.Multiple(() =>
        {
            Assert.That(render.VisibilityAnalysis!.VisiblePixelRatio, Is.GreaterThan(0));
            Assert.That(element.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(((KeyFrameAnimation<float>)rect.Opacity.GetKeyFrameAnimation()!).UseGlobalClock.Value, Is.False);
            Assert.That(((KeyFrameAnimation<float>)translate.Y.GetKeyFrameAnimation()!).UseGlobalClock.Value, Is.False);
            Assert.That(opacity, Is.EqualTo(50f).Within(0.001f));
            Assert.That(translateY, Is.EqualTo(50f).Within(0.001f));
        });
    }

    private static JsonObject PatchTextObject(Element element, TextBlock text, JsonObject textPatch)
    {
        textPatch[nameof(CoreObject.Id)] = text.Id.Value.ToString();
        return new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.Value.ToString(),
                ["Objects"] = new JsonArray(textPatch)
            })
        };
    }

    private static JsonObject CreateOpacityAnimationDocument()
        => CreateOpacityAnimationDocument(MediaTime.Zero, MediaTime.FromSeconds(1));

    private static JsonObject CreateOpacityAnimationDocument(MediaTime firstKeyTime, MediaTime secondKeyTime)
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(
            new KeyFrame<float>
            {
                KeyTime = { Value = firstKeyTime },
                Value = { Value = 0 },
                Easing = { Value = new LinearEasing() }
            },
            out _);
        animation.KeyFrames.Add(
            new KeyFrame<float>
            {
                KeyTime = { Value = secondKeyTime },
                Value = { Value = 100 },
                Easing = { Value = new SineEaseOut() }
            },
            out _);

        return SamplePatchBuilder.SerializeWithoutIds(animation);
    }

    private static JsonObject CreateLinearFloatAnimationDocument(float firstValue, float secondValue)
        => SamplePatchBuilder.SerializeWithoutIds(TestWorkspace.CreateLinearFloatAnimation(firstValue, secondValue));

    private static JsonObject CreateTransformGroupWithAnimatedTranslateYDocument()
    {
        var translate = new TranslateTransform();
        translate.Y.SetKeyFrameAnimation(TestWorkspace.CreateLinearFloatAnimation(0, 100));
        var transform = new TransformGroup { Children = { translate } };

        JsonObject transformJson = CoreSerializer.SerializeToJsonObject(transform);
        CollectionReconciler.RemoveIds(transformJson);
        return transformJson;
    }

    private static Scene CreateSceneWithText(out Element element, out TextBlock text)
    {
        Scene scene = TestWorkspace.CreateScene();
        element = TestWorkspace.AddElement(scene, MediaTime.Zero, MediaTime.FromSeconds(4));
        text = new TextBlock { Text = { Value = "Launch" } };
        element.AddObject(text);
        return scene;
    }
}
