using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Generative;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class DuplicateObjectTests
{
    [Test]
    public void Duplicate_object_appends_copy_with_fresh_ids()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var blur = new Blur();
        var group = new FilterEffectGroup { Children = { blur } };
        var rect = new RectShape
        {
            Name = { Value = "Original rect" },
            Width = { Value = 200 },
            Height = { Value = 100 },
            FilterEffect = { Value = group }
        };
        element.AddObject(rect);
        Guid originalRectId = rect.Id.Value;
        Guid originalGroupId = group.Id.Value;
        Guid originalBlurId = blur.Id.Value;

        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(originalRectId.ToString());

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var copy = (RectShape)element.Objects[1];
        var copyGroup = (FilterEffectGroup)copy.FilterEffect.Value!;

        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Valid, Is.True);
            Assert.That(element.Objects, Has.Count.EqualTo(2));
            Assert.That(element.Objects[0].Id.Value, Is.EqualTo(originalRectId));
            Assert.That(copy.Id.Value, Is.Not.EqualTo(originalRectId));
            Assert.That(result.Value.ObjectId, Is.EqualTo(copy.Id.Value.ToString()));
            Assert.That(result.Value.ElementId, Is.EqualTo(element.Id.Value.ToString()));
            Assert.That(copyGroup.Id.Value, Is.Not.EqualTo(originalGroupId));
            Assert.That(copyGroup.Children, Has.Count.EqualTo(1));
            Assert.That(copyGroup.Children[0], Is.InstanceOf<Blur>());
            Assert.That(copyGroup.Children[0].Id.Value, Is.Not.EqualTo(originalBlurId));
            Assert.That(result.Value.CreatedIds, Is.Not.Empty);
        });
    }

    [Test]
    public void Duplicate_object_is_undoable()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var rect = new RectShape { Name = { Value = "Original rect" }, Width = { Value = 120 } };
        element.AddObject(rect);

        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(rect.Id.Value.ToString());
        int afterDuplicate = element.Objects.Count;

        bool undone = session.History.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(afterDuplicate, Is.EqualTo(2));
            Assert.That(undone, Is.True);
            Assert.That(element.Objects, Has.Count.EqualTo(1));
            Assert.That(element.Objects[0].Id.Value, Is.EqualTo(rect.Id.Value));
        });
    }

    [Test]
    public void Duplicate_object_wrap_in_group_rejects_a_direct_flow_operator()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var group = new DrawableGroup { Name = { Value = "Direct group" } };
        group.Children.Add(new RectShape { Name = { Value = "inner" } });
        element.AddObject(group);
        int objectCountBefore = element.Objects.Count;

        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(group.Id.Value.ToString(), wrapInGroup: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Message, Does.Contain("flow operator"));
            // The rejected wrap must not have added a second group or orphaned the existing portal.
            Assert.That(element.Objects, Has.Count.EqualTo(objectCountBefore));
            Assert.That(element.Objects.OfType<DrawableGroup>().Single(), Is.SameAs(group));
        });
    }

    [Test]
    public void Duplicate_object_wrap_in_group_moves_original_and_copy_into_drawable_group()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var rect = new RectShape { Name = { Value = "Original rect" }, Width = { Value = 120 } };
        element.AddObject(rect);
        Guid originalId = rect.Id.Value;

        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(rect.Id.Value.ToString(), wrapInGroup: true);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var group = element.Objects.OfType<DrawableGroup>().Single();
        var copy = group.Children.Single(item => item.Id.Value.ToString() == result.Value!.ObjectId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Valid, Is.True);
            Assert.That(result.Value.ElementId, Is.EqualTo(element.Id.Value.ToString()));
            Assert.That(result.Value.GroupId, Is.EqualTo(group.Id.Value.ToString()));
            Assert.That(element.Objects.OfType<PortalObject>(), Has.Exactly(1).Items);
            Assert.That(element.Objects.OfType<DrawableGroup>(), Has.Exactly(1).Items);
            Assert.That(element.Objects.OfType<RectShape>(), Is.Empty);
            Assert.That(group.Children, Has.Count.EqualTo(2));
            Assert.That(group.Children[0], Is.SameAs(rect));
            Assert.That(group.Children[0].Id.Value, Is.EqualTo(originalId));
            Assert.That(copy, Is.InstanceOf<RectShape>());
            Assert.That(copy.Id.Value, Is.Not.EqualTo(originalId));
            Assert.That(group.Children.Select(item => item.Id.Value), Is.Unique);
            Assert.That(result.Value.ObjectId, Is.EqualTo(copy.Id.Value.ToString()));
            Assert.That(result.Value.CreatedIds.Select(item => item.Id), Does.Contain(copy.Id.Value.ToString()));
            Assert.That(result.Value.CreatedIds.Select(item => item.Id), Does.Contain(group.Id.Value.ToString()));
        });
    }

    [Test]
    public void Duplicate_object_wrap_in_group_appends_copy_to_existing_group_child()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var rect = new RectShape { Name = { Value = "Original rect" }, Width = { Value = 120 } };
        var group = new DrawableGroup { Name = { Value = "Existing group" } };
        group.Children.Add(rect);
        element.AddObject(group);
        Guid originalId = rect.Id.Value;
        Guid originalGroupId = group.Id.Value;

        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(rect.Id.Value.ToString(), wrapInGroup: true);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var copy = group.Children.Single(item => item.Id.Value.ToString() == result.Value!.ObjectId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Valid, Is.True);
            Assert.That(result.Value.GroupId, Is.EqualTo(originalGroupId.ToString()));
            Assert.That(element.Objects.OfType<DrawableGroup>(), Has.Exactly(1).Items);
            Assert.That(group.Children, Has.Count.EqualTo(2));
            Assert.That(group.Children[0], Is.SameAs(rect));
            Assert.That(group.Children[0].Id.Value, Is.EqualTo(originalId));
            Assert.That(copy.Id.Value, Is.Not.EqualTo(originalId));
            Assert.That(group.Children.Select(item => item.Id.Value), Is.Unique);
            Assert.That(result.Value.CreatedIds.Select(item => item.Id), Does.Contain(copy.Id.Value.ToString()));
            Assert.That(result.Value.CreatedIds.Select(item => item.Id), Does.Not.Contain(group.Id.Value.ToString()));
        });
    }

    [Test]
    public void Duplicate_object_wrap_in_group_is_undoable()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var rect = new RectShape { Name = { Value = "Original rect" }, Width = { Value = 120 } };
        element.AddObject(rect);
        Guid originalId = rect.Id.Value;

        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(rect.Id.Value.ToString(), wrapInGroup: true);
        int objectCountAfterDuplicate = element.Objects.Count;

        bool undone = session.History.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(objectCountAfterDuplicate, Is.EqualTo(2));
            Assert.That(undone, Is.True);
            Assert.That(element.Objects, Has.Count.EqualTo(1));
            Assert.That(element.Objects[0], Is.SameAs(rect));
            Assert.That(element.Objects[0].Id.Value, Is.EqualTo(originalId));
            Assert.That(rect.HierarchicalParent, Is.SameAs(element));
        });
    }

    [Test]
    public void Duplicate_object_wrap_in_group_mints_fresh_ids_on_nested_nodes()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var blur = new Blur();
        var effect = new FilterEffectGroup { Children = { blur } };
        var rect = new RectShape
        {
            Name = { Value = "Original rect" },
            Width = { Value = 120 },
            FilterEffect = { Value = effect }
        };
        element.AddObject(rect);
        Guid originalEffectId = effect.Id.Value;
        Guid originalBlurId = blur.Id.Value;

        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(rect.Id.Value.ToString(), wrapInGroup: true);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        DrawableGroup wrapper = element.Objects.OfType<DrawableGroup>().Single();
        var copy = (RectShape)wrapper.Children.Single(item => item.Id.Value.ToString() == result.Value!.ObjectId);
        var copyEffect = (FilterEffectGroup)copy.FilterEffect.Value!;

        Assert.Multiple(() =>
        {
            Assert.That(copyEffect.Id.Value, Is.Not.EqualTo(Guid.Empty));
            Assert.That(copyEffect.Id.Value, Is.Not.EqualTo(originalEffectId));
            Assert.That(copyEffect.Children, Has.Count.EqualTo(1));
            Assert.That(copyEffect.Children[0], Is.InstanceOf<Blur>());
            Assert.That(copyEffect.Children[0].Id.Value, Is.Not.EqualTo(Guid.Empty));
            Assert.That(copyEffect.Children[0].Id.Value, Is.Not.EqualTo(originalBlurId));
        });
    }

    [Test]
    public void Duplicate_object_wrap_in_group_keeps_the_media_of_an_element_in_a_subfolder()
    {
        // The element's file sits in a subfolder, so a media path written relative to it would resolve to
        // another file against the scene.
        Scene scene = TestWorkspace.CreateScene();
        string subDir = Directory.CreateDirectory(
            Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "sub")).FullName;
        Element element = TestWorkspace.AddElement(
            scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), Path.Combine("sub", "media.belm"));
        string imagePath = Path.Combine(subDir, "source.png");
        using (var bitmap = new Bitmap(8, 8))
        {
            Assert.That(bitmap.Save(imagePath, EncodedImageFormat.Png), Is.True);
        }

        var imageSource = new ImageSource();
        imageSource.ReadFrom(new Uri(imagePath));
        var image = new SourceImage { Source = { Value = imageSource } };
        element.AddObject(image);

        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(image.Id.Value.ToString(), wrapInGroup: true);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var copy = (SourceImage)element.Objects.OfType<DrawableGroup>().Single().Children
            .Single(item => item.Id.Value.ToString() == result.Value!.ObjectId);
        Assert.That(copy.Source.Value?.Uri?.LocalPath, Is.EqualTo(imagePath));
    }

    [Test]
    public void Duplicate_object_wrap_in_group_enters_the_session_dispatcher_once()
    {
        // The editor follows each dispatch that commits a transaction, so a second one would navigate twice.
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var rect = new RectShape { Name = { Value = "Original rect" }, Width = { Value = 120 } };
        element.AddObject(rect);

        using var session = new DispatchingTestSession(scene);
        var tools = new EditTools(TestSessions.CreateManager(session));

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(rect.Id.Value.ToString(), wrapInGroup: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(session.InvokeCount, Is.EqualTo(1));
            Assert.That(session.History.UndoCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Duplicate_object_unknown_id_is_stale_handle()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        element.AddObject(new RectShape { Name = { Value = "Original rect" } });

        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(Guid.NewGuid().ToString());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.StaleHandle));
        });
    }

    [Test]
    public void Duplicate_object_wrong_element_scope_is_stale_handle()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var rect = new RectShape { Name = { Value = "Original rect" } };
        element.AddObject(rect);

        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<DuplicateObjectResponse> result =
            tools.DuplicateObject(rect.Id.Value.ToString(), elementId: Guid.NewGuid().ToString());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.StaleHandle));
        });
    }

    // An AI node of a copy needs a request key of its own, or Regenerate on the copy returns the original's result.
    // apply_edit cannot take this graph yet (#3450), so the probe below covers the copy made without a group.
    [Test]
    public void Duplicate_object_wrap_in_group_gives_the_ai_nodes_of_the_copy_request_keys_of_their_own()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var drawable = new NodeGraphDrawable();
        var image = new AiImageGenerationNode();
        var group = new GroupNode();
        var nested = new AiImageGenerationNode();
        group.Group.Value.Nodes.Add(nested);
        drawable.Model.Value!.Nodes.Add(image);
        drawable.Model.Value.Nodes.Add(group);
        element.AddObject(drawable);
        using var session = new AgentToolkitTestSession(scene);

        ToolResult<DuplicateObjectResponse> result =
            session.CreateEditTools().DuplicateObject(drawable.Id.Value.ToString(), wrapInGroup: true);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        GraphModel graph = ((NodeGraphDrawable)FindCopy(element, result.Value!)).Model.Value!;
        var copiedImage = graph.Nodes.OfType<AiImageGenerationNode>().Single();
        var copiedNested = graph.Nodes.OfType<GroupNode>().Single().Group.Value.Nodes.OfType<AiImageGenerationNode>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(copiedImage.RequestKeySeed, Is.Not.EqualTo(image.RequestKeySeed));
            Assert.That(copiedNested.RequestKeySeed, Is.Not.EqualTo(nested.RequestKeySeed));
        });
    }

    // Each object of the copy renews what it must not share with the original, as an AI node renews its request key.
    [Test]
    public void Duplicate_object_renews_the_values_a_copy_must_not_share()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var probe = new SeededCopyProbe();
        element.AddObject(probe);
        using var session = new AgentToolkitTestSession(scene);

        ToolResult<DuplicateObjectResponse> result = session.CreateEditTools().DuplicateObject(probe.Id.Value.ToString());

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(((SeededCopyProbe)FindCopy(element, result.Value!)).Seed.Value, Is.Not.EqualTo(probe.Seed.Value));
    }

    // A copy holds a file-backed object itself, or saving it would write over the original's file.
    [TestCase(false)]
    [TestCase(true)]
    public void Duplicate_object_gives_the_copy_its_own_file_backed_objects(bool wrapInGroup)
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var file = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "width.json"));
        var rect = new RectShape();
        var animation = new KeyFrameAnimation<float> { Uri = file, KeyFrames = { new KeyFrame<float> { Value = { Value = 50 } } } };
        rect.Width.SetKeyFrameAnimation(animation);
        element.AddObject(rect);
        using var session = new AgentToolkitTestSession(scene);

        ToolResult<DuplicateObjectResponse> result =
            session.CreateEditTools().DuplicateObject(rect.Id.Value.ToString(), wrapInGroup: wrapInGroup);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        KeyFrameAnimation<float> copied = ((RectShape)FindCopy(element, result.Value!)).Width.GetKeyFrameAnimation()!;
        Assert.Multiple(() =>
        {
            Assert.That(copied.Uri, Is.Null);
            Assert.That(copied.KeyFrames.Single().Value, Is.EqualTo(50));
            Assert.That(rect.Width.GetKeyFrameAnimation()!.Uri, Is.EqualTo(file));
        });
    }

    // The copy a duplicate_object call made, on its element or in the group around the original.
    private static EngineObject FindCopy(Element element, DuplicateObjectResponse response)
    {
        return element.Objects
            .Concat(element.Objects.OfType<DrawableGroup>().SelectMany(group => group.Children))
            .Single(item => item.Id.Value.ToString() == response.ObjectId);
    }
}

// Renews its seed in a copy, as an AI node renews its request key.
[SuppressResourceClassGeneration]
public sealed class SeededCopyProbe : EngineObject, ICopyRenewable
{
    public SeededCopyProbe()
    {
        ScanProperties<SeededCopyProbe>();
    }

    public IProperty<string> Seed { get; } = Property.CreateValue(Guid.NewGuid().ToString("N"));

    void ICopyRenewable.RenewForCopy() => Seed.Value = Guid.NewGuid().ToString("N");
}
