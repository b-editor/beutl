using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class HistoryToolsTests
{
    [Test]
    public void Undo_reverts_the_last_apply_edit_and_redo_restores_it()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        (AgentToolkitTestSession session, EditTools edits, HistoryTools history) = CreateTools(scene);
        using AgentToolkitTestSession ownedSession = session;
        TimeSpan original = element.Start;

        ToolResult<ApplyEditResponse> apply = edits.ApplyEdit(
            patch: StartPatch(element, TimeSpan.FromSeconds(7)),
            schemaVersion: SchemaVersion.Current);
        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
        Assert.That(scene.Children.Single().Start, Is.EqualTo(TimeSpan.FromSeconds(7)));

        ToolResult<HistoryStateResponse> undo = history.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(undo.IsSuccess, Is.True, undo.Error?.Message);
            Assert.That(undo.Value!.Applied, Has.Count.EqualTo(1));
            Assert.That(scene.Children.Single().Start, Is.EqualTo(original));
            Assert.That(undo.Value.CanRedo, Is.True);
        });

        ToolResult<HistoryStateResponse> redo = history.Redo();
        Assert.Multiple(() =>
        {
            Assert.That(redo.IsSuccess, Is.True, redo.Error?.Message);
            Assert.That(redo.Value!.Applied, Has.Count.EqualTo(1));
            Assert.That(scene.Children.Single().Start, Is.EqualTo(TimeSpan.FromSeconds(7)));
        });
    }

    [Test]
    public void Undo_walks_back_multiple_transactions_and_stops_when_the_stack_empties()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        (AgentToolkitTestSession session, EditTools edits, HistoryTools history) = CreateTools(scene);
        using AgentToolkitTestSession ownedSession = session;
        TimeSpan original = element.Start;

        foreach (int seconds in new[] { 4, 5, 6 })
        {
            ToolResult<ApplyEditResponse> apply = edits.ApplyEdit(
                patch: StartPatch(element, TimeSpan.FromSeconds(seconds)),
                schemaVersion: SchemaVersion.Current);
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
        }

        // More steps than transactions: undo must drain what exists and report the shortfall
        // instead of failing, so a caller can back out "everything I just did" in one call.
        ToolResult<HistoryStateResponse> undo = history.Undo(steps: 10);

        Assert.Multiple(() =>
        {
            Assert.That(undo.IsSuccess, Is.True, undo.Error?.Message);
            Assert.That(undo.Value!.Applied, Has.Count.EqualTo(3));
            Assert.That(undo.Value.CanUndo, Is.False);
            Assert.That(undo.Value.UndoCount, Is.EqualTo(0));
            Assert.That(undo.Value.Message, Does.Contain("the stack emptied first"));
            Assert.That(scene.Children.Single().Start, Is.EqualTo(original));
        });
    }

    [Test]
    public void Undo_on_an_empty_history_succeeds_and_reports_that_nothing_moved()
    {
        Scene scene = CreateSceneWithElement(out _);
        (AgentToolkitTestSession session, _, HistoryTools history) = CreateTools(scene);
        using AgentToolkitTestSession ownedSession = session;

        ToolResult<HistoryStateResponse> undo = history.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(undo.IsSuccess, Is.True, undo.Error?.Message);
            Assert.That(undo.Value!.Applied, Is.Empty);
            Assert.That(undo.Value.CanUndo, Is.False);
            Assert.That(undo.Value.Message, Does.Contain("Nothing to undo"));
        });
    }

    [Test]
    public void Read_history_names_the_next_undo_without_changing_the_scene()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        (AgentToolkitTestSession session, EditTools edits, HistoryTools history) = CreateTools(scene);
        using AgentToolkitTestSession ownedSession = session;

        edits.ApplyEdit(patch: StartPatch(element, TimeSpan.FromSeconds(9)), schemaVersion: SchemaVersion.Current);

        ToolResult<HistoryStateResponse> state = history.ReadHistory();

        Assert.Multiple(() =>
        {
            Assert.That(state.IsSuccess, Is.True, state.Error?.Message);
            Assert.That(state.Value!.Applied, Is.Empty);
            Assert.That(state.Value.CanUndo, Is.True);
            Assert.That(state.Value.UndoCount, Is.EqualTo(1));
            Assert.That(state.Value.NextUndo, Is.Not.Null);
            Assert.That(scene.Children.Single().Start, Is.EqualTo(TimeSpan.FromSeconds(9)));
        });
    }

    [Test]
    public void A_new_edit_after_undo_clears_the_redo_stack()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        (AgentToolkitTestSession session, EditTools edits, HistoryTools history) = CreateTools(scene);
        using AgentToolkitTestSession ownedSession = session;

        edits.ApplyEdit(patch: StartPatch(element, TimeSpan.FromSeconds(4)), schemaVersion: SchemaVersion.Current);
        history.Undo();
        edits.ApplyEdit(patch: StartPatch(element, TimeSpan.FromSeconds(5)), schemaVersion: SchemaVersion.Current);

        ToolResult<HistoryStateResponse> redo = history.Redo();

        Assert.Multiple(() =>
        {
            Assert.That(redo.IsSuccess, Is.True, redo.Error?.Message);
            Assert.That(redo.Value!.Applied, Is.Empty);
            Assert.That(redo.Value.Message, Does.Contain("Nothing to redo"));
            Assert.That(scene.Children.Single().Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
        });
    }

    private static (AgentToolkitTestSession Session, EditTools Edits, HistoryTools History) CreateTools(Scene scene)
    {
        var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        return (session, new EditTools(manager), new HistoryTools(manager));
    }

    private static JsonObject StartPatch(Element element, TimeSpan start)
    {
        return new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                [nameof(Element.Start)] = start.ToString("c")
            })
        };
    }

    private static Scene CreateSceneWithElement(out Element element)
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scene = new Scene(1920, 1080, "Scene") { Uri = new Uri(Path.Combine(dir, "Scene.scene")) };
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
