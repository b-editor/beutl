using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class DuplicateObjectTests
{
    [Test]
    public void Duplicate_object_appends_copy_with_fresh_ids()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        var blur = new Blur();
        var group = new FilterEffectGroup { Children = { blur } };
        var rect = new RectShape
        {
            Name = "Original rect",
            Width = { CurrentValue = 200 },
            Height = { CurrentValue = 100 },
            FilterEffect = { CurrentValue = group }
        };
        element.AddObject(rect);
        Guid originalRectId = rect.Id;
        Guid originalGroupId = group.Id;
        Guid originalBlurId = blur.Id;

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(originalRectId.ToString());

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var copy = (RectShape)element.Objects[1];
        var copyGroup = (FilterEffectGroup)copy.FilterEffect.CurrentValue!;

        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Valid, Is.True);
            Assert.That(element.Objects, Has.Count.EqualTo(2));
            Assert.That(element.Objects[0].Id, Is.EqualTo(originalRectId));
            Assert.That(copy.Id, Is.Not.EqualTo(originalRectId));
            Assert.That(result.Value.ObjectId, Is.EqualTo(copy.Id.ToString()));
            Assert.That(result.Value.ElementId, Is.EqualTo(element.Id.ToString()));
            Assert.That(copyGroup.Id, Is.Not.EqualTo(originalGroupId));
            Assert.That(copyGroup.Children, Has.Count.EqualTo(1));
            Assert.That(copyGroup.Children[0], Is.InstanceOf<Blur>());
            Assert.That(copyGroup.Children[0].Id, Is.Not.EqualTo(originalBlurId));
            Assert.That(result.Value.CreatedIds, Is.Not.Empty);
        });
    }

    [Test]
    public void Duplicate_object_is_undoable()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        var rect = new RectShape { Name = "Original rect", Width = { CurrentValue = 120 } };
        element.AddObject(rect);

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(rect.Id.ToString());
        int afterDuplicate = element.Objects.Count;

        bool undone = session.History.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(afterDuplicate, Is.EqualTo(2));
            Assert.That(undone, Is.True);
            Assert.That(element.Objects, Has.Count.EqualTo(1));
            Assert.That(element.Objects[0].Id, Is.EqualTo(rect.Id));
        });
    }

    [Test]
    public void Duplicate_object_wrap_in_group_moves_original_and_copy_into_drawable_group()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        var rect = new RectShape { Name = "Original rect", Width = { CurrentValue = 120 } };
        element.AddObject(rect);
        Guid originalId = rect.Id;

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(rect.Id.ToString(), wrapInGroup: true);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var group = element.Objects.OfType<DrawableGroup>().Single();
        var copy = group.Children.Single(item => item.Id.ToString() == result.Value!.ObjectId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Valid, Is.True);
            Assert.That(result.Value.ElementId, Is.EqualTo(element.Id.ToString()));
            Assert.That(result.Value.GroupId, Is.EqualTo(group.Id.ToString()));
            Assert.That(element.Objects.OfType<PortalObject>(), Has.Exactly(1).Items);
            Assert.That(element.Objects.OfType<DrawableGroup>(), Has.Exactly(1).Items);
            Assert.That(element.Objects.OfType<RectShape>(), Is.Empty);
            Assert.That(group.Children, Has.Count.EqualTo(2));
            Assert.That(group.Children[0], Is.SameAs(rect));
            Assert.That(group.Children[0].Id, Is.EqualTo(originalId));
            Assert.That(copy, Is.InstanceOf<RectShape>());
            Assert.That(copy.Id, Is.Not.EqualTo(originalId));
            Assert.That(group.Children.Select(item => item.Id), Is.Unique);
            Assert.That(result.Value.ObjectId, Is.EqualTo(copy.Id.ToString()));
            Assert.That(result.Value.CreatedIds.Select(item => item.Id), Does.Contain(copy.Id.ToString()));
            Assert.That(result.Value.CreatedIds.Select(item => item.Id), Does.Contain(group.Id.ToString()));
        });
    }

    [Test]
    public void Duplicate_object_wrap_in_group_appends_copy_to_existing_group_child()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        var rect = new RectShape { Name = "Original rect", Width = { CurrentValue = 120 } };
        var group = new DrawableGroup { Name = "Existing group" };
        group.Children.Add(rect);
        element.AddObject(group);
        Guid originalId = rect.Id;
        Guid originalGroupId = group.Id;

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(rect.Id.ToString(), wrapInGroup: true);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var copy = group.Children.Single(item => item.Id.ToString() == result.Value!.ObjectId);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Valid, Is.True);
            Assert.That(result.Value.GroupId, Is.EqualTo(originalGroupId.ToString()));
            Assert.That(element.Objects.OfType<DrawableGroup>(), Has.Exactly(1).Items);
            Assert.That(group.Children, Has.Count.EqualTo(2));
            Assert.That(group.Children[0], Is.SameAs(rect));
            Assert.That(group.Children[0].Id, Is.EqualTo(originalId));
            Assert.That(copy.Id, Is.Not.EqualTo(originalId));
            Assert.That(group.Children.Select(item => item.Id), Is.Unique);
            Assert.That(result.Value.CreatedIds.Select(item => item.Id), Does.Contain(copy.Id.ToString()));
            Assert.That(result.Value.CreatedIds.Select(item => item.Id), Does.Not.Contain(group.Id.ToString()));
        });
    }

    [Test]
    public void Duplicate_object_wrap_in_group_is_undoable()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        var rect = new RectShape { Name = "Original rect", Width = { CurrentValue = 120 } };
        element.AddObject(rect);
        Guid originalId = rect.Id;

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(rect.Id.ToString(), wrapInGroup: true);
        int objectCountAfterDuplicate = element.Objects.Count;

        bool undone = session.History.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(objectCountAfterDuplicate, Is.EqualTo(2));
            Assert.That(undone, Is.True);
            Assert.That(element.Objects, Has.Count.EqualTo(1));
            Assert.That(element.Objects[0], Is.SameAs(rect));
            Assert.That(element.Objects[0].Id, Is.EqualTo(originalId));
            Assert.That(rect.HierarchicalParent, Is.SameAs(element));
        });
    }

    [Test]
    public async Task Duplicate_object_wrap_in_group_does_not_raise_element_structure_major_issue()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        var rect = new RectShape { Name = "[role:decorative] bloom source", Width = { CurrentValue = 120 } };
        element.AddObject(rect);

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(rect.Id.ToString(), wrapInGroup: true);
        QualityReviewResponse quality = await AnalyzeAsync(scene);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(quality.Issues, Has.None.Matches<QualityIssue>(issue =>
                issue.Category == "elementStructure"
                && issue.Severity == "major"));
        });
    }

    [Test]
    public void Duplicate_object_wrap_in_group_mints_fresh_ids_on_nested_nodes()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        var blur = new Blur();
        var effect = new FilterEffectGroup { Children = { blur } };
        var rect = new RectShape
        {
            Name = "Original rect",
            Width = { CurrentValue = 120 },
            FilterEffect = { CurrentValue = effect }
        };
        element.AddObject(rect);
        Guid originalEffectId = effect.Id;
        Guid originalBlurId = blur.Id;

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        ToolResult<DuplicateObjectResponse> result = tools.DuplicateObject(rect.Id.ToString(), wrapInGroup: true);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        DrawableGroup wrapper = element.Objects.OfType<DrawableGroup>().Single();
        var copy = (RectShape)wrapper.Children.Single(item => item.Id.ToString() == result.Value!.ObjectId);
        var copyEffect = (FilterEffectGroup)copy.FilterEffect.CurrentValue!;

        Assert.Multiple(() =>
        {
            Assert.That(copyEffect.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(copyEffect.Id, Is.Not.EqualTo(originalEffectId));
            Assert.That(copyEffect.Children, Has.Count.EqualTo(1));
            Assert.That(copyEffect.Children[0], Is.InstanceOf<Blur>());
            Assert.That(copyEffect.Children[0].Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(copyEffect.Children[0].Id, Is.Not.EqualTo(originalBlurId));
        });
    }

    [Test]
    public void Duplicate_object_unknown_id_is_stale_handle()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        element.AddObject(new RectShape { Name = "Original rect" });

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

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
        Scene scene = CreateSceneWithElement(out Element element);
        var rect = new RectShape { Name = "Original rect" };
        element.AddObject(rect);

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new EditTools(manager);

        ToolResult<DuplicateObjectResponse> result =
            tools.DuplicateObject(rect.Id.ToString(), elementId: Guid.NewGuid().ToString());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.StaleHandle));
        });
    }

    private static Scene CreateSceneWithElement(out Element element)
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Scene scene = new(1920, 1080, "Scene")
        {
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        element = new Element
        {
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(dir, "element.belm"))
        };
        scene.Children.Add(element);
        return scene;
    }

    private static ValueTask<QualityReviewResponse> AnalyzeAsync(Scene scene)
    {
        var stillRenderer = new StillRenderer();
        return new QualityAnalyzer(new MotionVariationAnalyzer(stillRenderer), stillRenderer).AnalyzeAsync(
            scene,
            timeSeconds: null,
            sampleCount: 3,
            renderScale: 1,
            styleProfile: null,
            allowAllCaps: false,
            allowHardCuts: false,
            allowRectDominance: false,
            relaxAesthetics: false,
            allowStillness: false,
            allowDenseText: false,
            allowMultiObjectElements: false,
            allowMonochrome: false,
            allowMinimalDensity: false,
            plannedForegroundElementsPerShot: 0,
            evaluateMotion: false,
            cancellationToken: CancellationToken.None);
    }
}
