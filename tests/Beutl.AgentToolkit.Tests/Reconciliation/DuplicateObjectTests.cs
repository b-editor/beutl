using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
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
}
