using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Documents;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class KeyFrameShorthandTests
{
    [Test]
    public void Shorthand_tuples_expand_into_a_typed_keyframe_animation()
    {
        (EditTools tools, Scene scene, Element element) = CreateSceneWithRect();

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: OpacityPatch(element, new JsonArray(
                new JsonArray(0, 0, "CubicEaseOut"),
                new JsonArray(0.4, 100))),
            schemaVersion: SchemaVersion.Current);

        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);

        IAnimation animation = RequireOpacityAnimation(scene);
        var keyFrames = ((KeyFrameAnimation)animation).KeyFrames;

        Assert.Multiple(() =>
        {
            Assert.That(animation.ValueType, Is.EqualTo(typeof(float)));
            Assert.That(keyFrames, Has.Count.EqualTo(2));
            Assert.That(keyFrames[0].KeyTime, Is.EqualTo(TimeSpan.Zero));
            Assert.That(keyFrames[0].Easing, Is.TypeOf<CubicEaseOut>());
            Assert.That(keyFrames[1].KeyTime, Is.EqualTo(TimeSpan.FromSeconds(0.4)));
        });
    }

    [Test]
    public void Shorthand_objects_are_accepted_alongside_sibling_animation_properties()
    {
        (EditTools tools, Scene scene, Element element) = CreateSceneWithRect();

        JsonObject patch = OpacityPatch(element, new JsonArray(
            new JsonObject { ["t"] = 0, ["v"] = 0 },
            new JsonObject { ["t"] = "00:00:01", ["v"] = 100, ["easing"] = "BackEaseOut" }));
        patch["Elements"]![0]!["Objects"]![0]!["Animations"]!["Opacity"]!["UseGlobalClock"] = true;

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);

        var animation = (KeyFrameAnimation)RequireOpacityAnimation(scene);

        Assert.Multiple(() =>
        {
            Assert.That(animation.UseGlobalClock, Is.True);
            Assert.That(animation.KeyFrames, Has.Count.EqualTo(2));
            Assert.That(animation.KeyFrames[1].KeyTime, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(animation.KeyFrames[1].Easing, Is.TypeOf<BackEaseOut>());
        });
    }

    [Test]
    public void An_unknown_easing_is_rejected_by_name_with_the_accepted_form()
    {
        (EditTools tools, _, Element element) = CreateSceneWithRect();

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: OpacityPatch(element, new JsonArray(new JsonArray(0, 0, "SwooshEaseOut"))),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.False);
            Assert.That(apply.Error!.Message, Does.Contain("SwooshEaseOut"));
            Assert.That(apply.Error.Hint, Does.Contain("no assembly prefix"));
        });
    }

    [Test]
    public void A_malformed_entry_is_rejected_with_its_index()
    {
        (EditTools tools, _, Element element) = CreateSceneWithRect();

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: OpacityPatch(element, new JsonArray(new JsonArray(0, 0), "nonsense")),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.False);
            Assert.That(apply.Error!.Message, Does.Contain("Keyframe 1"));
        });
    }

    [Test]
    public void Keyframes_do_not_inflate_createdIds()
    {
        (EditTools tools, _, Element element) = CreateSceneWithRect();

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: OpacityPatch(element, new JsonArray(
                new JsonArray(0, 0),
                new JsonArray(0.5, 50),
                new JsonArray(1.0, 100),
                new JsonArray(1.5, 0))),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            // Four keyframes plus their animation would previously appear here with full paths,
            // none of which can be used as a follow-up handle.
            Assert.That(apply.Value!.CreatedIds.Select(item => item.Path), Has.None.Contains("KeyFrames"));
            Assert.That(apply.Value.CreatedIds.Select(item => item.Path), Has.None.Contains("Animations"));
        });
    }

    [Test]
    public void Shorthand_replaces_an_existing_animation_rather_than_merging_into_it()
    {
        (EditTools tools, Scene scene, Element element) = CreateSceneWithRect();

        tools.ApplyEdit(
            patch: OpacityPatch(element, new JsonArray(new JsonArray(0, 0), new JsonArray(1.0, 100))),
            schemaVersion: SchemaVersion.Current);

        // A merge-patch over the existing animation retains its KeyFrames member next to $kf.
        // The expansion must win, or apply_edit reports success while keeping the old envelope.
        ToolResult<ApplyEditResponse> second = tools.ApplyEdit(
            patch: OpacityPatch(element, new JsonArray(new JsonArray(0, 100), new JsonArray(0.5, 25))),
            schemaVersion: SchemaVersion.Current);

        var animation = (KeyFrameAnimation)RequireOpacityAnimation(scene);

        Assert.Multiple(() =>
        {
            Assert.That(second.IsSuccess, Is.True, second.Error?.Message);
            Assert.That(animation.KeyFrames, Has.Count.EqualTo(2));
            Assert.That(animation.KeyFrames[^1].KeyTime, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
        });
    }

    [TestCase(1e20)]
    [TestCase(double.NaN)]
    public void An_unrepresentable_time_is_rejected_rather_than_thrown(double seconds)
    {
        (EditTools tools, _, Element element) = CreateSceneWithRect();

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: OpacityPatch(element, new JsonArray(new JsonArray(seconds, 0))),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.False);
            Assert.That(apply.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(apply.Error.Message, Does.Contain("Keyframe 0"));
        });
    }

    [Test]
    public void A_non_string_easing_is_rejected_with_the_keyframe_index()
    {
        (EditTools tools, _, Element element) = CreateSceneWithRect();

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: OpacityPatch(element, new JsonArray(new JsonArray(0, 0), new JsonArray(1, 100, 3))),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.False);
            Assert.That(apply.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(apply.Error.Message, Does.Contain("Keyframe 1"));
            Assert.That(apply.Error.Hint, Does.Contain("bare type name"));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Out_of_range_animation_values_are_reported_and_coerced_by_the_owning_property(bool shorthand)
    {
        (EditTools tools, Scene scene, Element element) = CreateSceneWithRect();
        JsonObject animation = shorthand
            ? new JsonObject
            {
                [KeyFrameShorthand.PropertyName] = new JsonArray(
                    new JsonArray(0, -25),
                    new JsonArray(1, 125))
            }
            : new JsonObject
            {
                ["$type"] = IdentityHelper.WriteDiscriminator(typeof(KeyFrameAnimation<float>)),
                [nameof(KeyFrameAnimation.KeyFrames)] = new JsonArray(
                    CreateLongFormKeyFrame(0, -25),
                    CreateLongFormKeyFrame(1, 125))
            };

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: OpacityAnimationPatch(element, animation),
            schemaVersion: SchemaVersion.Current);

        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
        var keyFrames = ((KeyFrameAnimation)RequireOpacityAnimation(scene)).KeyFrames;

        Assert.Multiple(() =>
        {
            Assert.That(
                apply.Value!.Validation!.Count(outcome => outcome.Status == ValidationStatus.Coerced),
                Is.EqualTo(2));
            Assert.That(keyFrames.Select(keyFrame => keyFrame.Value), Is.EqualTo(new object[] { 0f, 100f }));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Relative_out_of_range_times_warn_for_shorthand_and_long_form(bool shorthand)
    {
        (EditTools tools, _, Element element) = CreateSceneWithRect();
        JsonObject animation = shorthand
            ? new JsonObject
            {
                [KeyFrameShorthand.PropertyName] = new JsonArray(new JsonArray(3, 100))
            }
            : new JsonObject
            {
                ["$type"] = IdentityHelper.WriteDiscriminator(typeof(KeyFrameAnimation<float>)),
                [nameof(KeyFrameAnimation.KeyFrames)] = new JsonArray(CreateLongFormKeyFrame(3, 100))
            };

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: OpacityAnimationPatch(element, animation),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(apply.Value!.Validation, Has.One.Matches<ValidationOutcome>(outcome =>
                outcome.Status == ValidationStatus.Warning
                && outcome.Message!.Contains("00:00:03", StringComparison.Ordinal)
                && outcome.Message.Contains("outside Element", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void Replacement_shorthand_range_warning_uses_the_new_envelope()
    {
        (EditTools tools, _, Element element) = CreateSceneWithRect();
        tools.ApplyEdit(
            patch: OpacityPatch(element, new JsonArray(new JsonArray(0, 0), new JsonArray(1, 100))),
            schemaVersion: SchemaVersion.Current);

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: OpacityPatch(element, new JsonArray(new JsonArray(0, 0), new JsonArray(3, 100))),
            schemaVersion: SchemaVersion.Current);

        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
        Assert.That(apply.Value!.Validation, Has.One.Matches<ValidationOutcome>(outcome =>
            outcome.Status == ValidationStatus.Warning
            && outcome.Message!.Contains("00:00:03", StringComparison.Ordinal)));
    }

    private static IAnimation RequireOpacityAnimation(Scene scene)
    {
        var shape = (RectShape)scene.Children.Single().Objects.Single();
        IProperty property = shape.Properties.Single(item => item.Name == nameof(RectShape.Opacity));
        Assert.That(property.Animation, Is.Not.Null);
        return property.Animation!;
    }

    private static JsonObject OpacityPatch(Element element, JsonArray keyframes)
        => OpacityAnimationPatch(
            element,
            new JsonObject { [KeyFrameShorthand.PropertyName] = keyframes });

    private static JsonObject OpacityAnimationPatch(Element element, JsonObject animation)
    {
        return new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                ["Objects"] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = element.Objects.Single().Id.ToString(),
                    ["Animations"] = new JsonObject
                    {
                        ["Opacity"] = animation
                    }
                })
            })
        };
    }

    private static JsonObject CreateLongFormKeyFrame(double seconds, float value)
    {
        return new JsonObject
        {
            ["$type"] = IdentityHelper.WriteDiscriminator(typeof(KeyFrame<float>)),
            [nameof(KeyFrame.KeyTime)] = TimeSpan.FromSeconds(seconds).ToString("c"),
            [nameof(KeyFrame<float>.Value)] = value
        };
    }

    private static (EditTools Tools, Scene Scene, Element Element) CreateSceneWithRect()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scene = new Scene(1920, 1080, "Scene") { Uri = new Uri(Path.Combine(dir, "Scene.scene")) };
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(dir, "element.belm"))
        };
        element.AddObject(new RectShape { Name = "plate" });
        scene.Children.Add(element);

        var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        return (new EditTools(manager), scene, element);
    }
}
