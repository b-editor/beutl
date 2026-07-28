using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
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

    private static IAnimation RequireOpacityAnimation(Scene scene)
    {
        var shape = (RectShape)scene.Children.Single().Objects.Single();
        IProperty property = shape.Properties.Single(item => item.Name == nameof(RectShape.Opacity));
        Assert.That(property.Animation, Is.Not.Null);
        return property.Animation!;
    }

    private static JsonObject OpacityPatch(Element element, JsonArray keyframes)
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
                        ["Opacity"] = new JsonObject { ["$kf"] = keyframes }
                    }
                })
            })
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
