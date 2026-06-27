using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class KeyframeEffectStructureTests
{
    [Test]
    public void Structural_element_operations_are_atomic_and_undoable()
    {
        Scene scene = CreateSceneWithElements(out Element first, out Element second);
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new ElementTools(manager);

        var moved = tools.MoveElement(first.Id.ToString(), startSeconds: 2, durationSeconds: 3, zIndex: 4);
        var duplicated = tools.DuplicateElement(first.Id.ToString(), startSeconds: 6);
        var split = tools.SplitElement(second.Id.ToString(), splitOffsetSeconds: 1);
        var deleteRejected = tools.RemoveElement(first.Id.ToString(), confirmDelete: false);
        var deleted = tools.RemoveElement(first.Id.ToString(), confirmDelete: true);

        Assert.Multiple(() =>
        {
            Assert.That(moved.IsSuccess, Is.True);
            Assert.That(duplicated.IsSuccess, Is.True, duplicated.Error?.Message);
            Assert.That(split.IsSuccess, Is.True, split.Error?.Message);
            Assert.That(deleteRejected.IsSuccess, Is.False);
            Assert.That(deleteRejected.Error!.Code, Is.EqualTo(ErrorCode.DestructiveIntent));
            Assert.That(deleted.IsSuccess, Is.True, deleted.Error?.Message);
            Assert.That(scene.Children.Any(element => element.Id == first.Id), Is.False);
            Assert.That(scene.Children.Count, Is.EqualTo(3));
        });

        session.History.Undo();
        Assert.That(scene.Children.Any(element => element.Id == first.Id), Is.True);
    }

    [Test]
    public void Group_and_ungroup_update_scene_groups()
    {
        Scene scene = CreateSceneWithElements(out Element first, out Element second);
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new ElementTools(manager);

        var grouped = tools.GroupElements([first.Id.ToString(), second.Id.ToString()]);
        var ungrouped = tools.UngroupElements([first.Id.ToString()]);

        Assert.Multiple(() =>
        {
            Assert.That(grouped.IsSuccess, Is.True);
            Assert.That(ungrouped.IsSuccess, Is.True);
            Assert.That(scene.Groups, Is.Empty);
        });
    }

    private static Scene CreateSceneWithElements(out Element first, out Element second)
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scene = new Scene(1920, 1080, "Scene")
        {
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        first = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(4),
            Uri = new Uri(Path.Combine(dir, "first.belm"))
        };
        second = new Element
        {
            Start = TimeSpan.FromSeconds(5),
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(dir, "second.belm"))
        };
        scene.Children.Add(first);
        scene.Children.Add(second);
        return scene;
    }
}
