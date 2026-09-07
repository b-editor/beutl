using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Editor;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class HistoryToolsTests
{
    [Test]
    public async Task Undo_reverts_the_last_apply_edit_and_redo_restores_it()
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

        ToolResult<HistoryStateResponse> undo = await history.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(undo.IsSuccess, Is.True, undo.Error?.Message);
            Assert.That(undo.Value!.Applied, Has.Count.EqualTo(1));
            Assert.That(scene.Children.Single().Start, Is.EqualTo(original));
            Assert.That(undo.Value.CanRedo, Is.True);
        });

        ToolResult<HistoryStateResponse> redo = await history.Redo();
        Assert.Multiple(() =>
        {
            Assert.That(redo.IsSuccess, Is.True, redo.Error?.Message);
            Assert.That(redo.Value!.Applied, Has.Count.EqualTo(1));
            Assert.That(scene.Children.Single().Start, Is.EqualTo(TimeSpan.FromSeconds(7)));
        });
    }

    [Test]
    public async Task Undo_walks_back_multiple_transactions_and_stops_when_the_stack_empties()
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
        ToolResult<HistoryStateResponse> undo = await history.Undo(steps: 10);

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
    public async Task Undo_on_an_empty_history_succeeds_and_reports_that_nothing_moved()
    {
        Scene scene = CreateSceneWithElement(out _);
        (AgentToolkitTestSession session, _, HistoryTools history) = CreateTools(scene);
        using AgentToolkitTestSession ownedSession = session;

        ToolResult<HistoryStateResponse> undo = await history.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(undo.IsSuccess, Is.True, undo.Error?.Message);
            Assert.That(undo.Value!.Applied, Is.Empty);
            Assert.That(undo.Value.CanUndo, Is.False);
            Assert.That(undo.Value.Message, Does.Contain("Nothing to undo"));
        });
    }

    [Test]
    public async Task Read_history_names_the_next_undo_without_changing_the_scene()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        (AgentToolkitTestSession session, EditTools edits, HistoryTools history) = CreateTools(scene);
        using AgentToolkitTestSession ownedSession = session;

        edits.ApplyEdit(patch: StartPatch(element, TimeSpan.FromSeconds(9)), schemaVersion: SchemaVersion.Current);

        ToolResult<HistoryStateResponse> state = await history.ReadHistory();

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
    public async Task A_new_edit_after_undo_clears_the_redo_stack()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        (AgentToolkitTestSession session, EditTools edits, HistoryTools history) = CreateTools(scene);
        using AgentToolkitTestSession ownedSession = session;

        edits.ApplyEdit(patch: StartPatch(element, TimeSpan.FromSeconds(4)), schemaVersion: SchemaVersion.Current);
        await history.Undo();
        edits.ApplyEdit(patch: StartPatch(element, TimeSpan.FromSeconds(5)), schemaVersion: SchemaVersion.Current);

        ToolResult<HistoryStateResponse> redo = await history.Redo();

        Assert.Multiple(() =>
        {
            Assert.That(redo.IsSuccess, Is.True, redo.Error?.Message);
            Assert.That(redo.Value!.Applied, Is.Empty);
            Assert.That(redo.Value.Message, Does.Contain("Nothing to redo"));
            Assert.That(scene.Children.Single().Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task History_move_reports_the_post_move_snapshot_from_the_same_dispatch(bool redo)
    {
        Scene scene = CreateSceneWithElement(out Element element);
        using var inner = new AgentToolkitTestSession(scene, EditingSessionSource.LiveEditor);
        var session = new InterleavingLiveSession(inner);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var edits = new EditTools(manager);
        var history = new HistoryTools(manager);

        edits.ApplyEdit(
            patch: StartPatch(element, TimeSpan.FromSeconds(4)),
            schemaVersion: SchemaVersion.Current);
        if (redo)
        {
            await history.Undo();
        }

        int dispatchesBeforeMove = session.InvokeCount;
        session.AfterNextInvoke = () => session.History.ExecuteInTransaction(
            () => element.Length = TimeSpan.FromSeconds(3),
            "Human edit after history move");

        ToolResult<HistoryStateResponse> move = redo ? await history.Redo() : await history.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(move.IsSuccess, Is.True, move.Error?.Message);
            Assert.That(move.Value!.Applied, Has.Count.EqualTo(1));
            Assert.That(move.Value.CanUndo, Is.EqualTo(redo));
            Assert.That(move.Value.UndoCount, Is.EqualTo(redo ? 1 : 0));
            Assert.That(move.Value.NextUndo, redo ? Is.Not.Null : Is.Null);
            Assert.That(move.Value.CanRedo, Is.EqualTo(!redo));
            Assert.That(move.Value.RedoCount, Is.EqualTo(redo ? 0 : 1));
            Assert.That(move.Value.NextRedo, redo ? Is.Null : Is.Not.Null);
            Assert.That(session.InvokeCount - dispatchesBeforeMove, Is.EqualTo(1));

            // The hook proves a later editor transaction really did land after the returned
            // point-in-time snapshot; it must not leak into the history response.
            Assert.That(session.History.UndoCount, Is.EqualTo(redo ? 2 : 1));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
        });
    }

    [Test]
    public async Task Live_undo_stops_playback_before_mutating_history()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        using RecordingPipeline recording = RecordingPipeline.Create(scene);
        var binding = new PlaybackSensitiveLiveBinding(scene, recording.History);
        var source = new LiveSessionSource();
        source.Attach(binding);
        var manager = new AgentSessionManager();
        manager.UseSource(source);
        var history = new HistoryTools(manager);
        recording.History.ExecuteInTransaction(
            () => element.Start = TimeSpan.FromSeconds(7),
            "edit before live undo");
        binding.IsPlaying = true;

        ToolResult<HistoryStateResponse> undo = await history.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(undo.IsSuccess, Is.True, undo.Error?.Message);
            Assert.That(binding.IsPlaying, Is.False);
            Assert.That(binding.PauseCount, Is.EqualTo(1));
            Assert.That(binding.GuardedInvocationCount, Is.EqualTo(1));
            Assert.That(binding.HistoryMutatedWhilePlaying, Is.False);
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public async Task Live_read_history_pauses_before_flushing_pending_work()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        using RecordingPipeline recording = RecordingPipeline.Create(scene);
        var binding = new PlaybackSensitiveLiveBinding(scene, recording.History) { IsPlaying = true };
        var source = new LiveSessionSource();
        source.Attach(binding);
        var manager = new AgentSessionManager();
        manager.UseSource(source);
        var history = new HistoryTools(manager);
        element.Start = TimeSpan.FromSeconds(7);
        Assert.That(recording.History.HasPendingOperations, Is.True);

        ToolResult<HistoryStateResponse> state = await history.ReadHistory();

        Assert.Multiple(() =>
        {
            Assert.That(state.IsSuccess, Is.True, state.Error?.Message);
            Assert.That(binding.IsPlaying, Is.False);
            Assert.That(binding.PauseCount, Is.EqualTo(1));
            Assert.That(binding.PendingWorkFlushedWhilePlaying, Is.False);
            Assert.That(binding.GuardedInvocationCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Live_read_history_with_no_pending_work_does_not_stop_playback()
    {
        Scene scene = CreateSceneWithElement(out _);
        using RecordingPipeline recording = RecordingPipeline.Create(scene);
        var binding = new PlaybackSensitiveLiveBinding(scene, recording.History) { IsPlaying = true };
        var source = new LiveSessionSource();
        source.Attach(binding);
        var manager = new AgentSessionManager();
        manager.UseSource(source);

        ToolResult<HistoryStateResponse> state = await new HistoryTools(manager).ReadHistory();

        Assert.Multiple(() =>
        {
            Assert.That(state.IsSuccess, Is.True, state.Error?.Message);
            Assert.That(binding.IsPlaying, Is.True);
            Assert.That(binding.PauseCount, Is.Zero);
            Assert.That(binding.GuardedInvocationCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Live_multi_step_undo_uses_one_guarded_batch()
    {
        Scene scene = CreateSceneWithElement(out Element element);
        using RecordingPipeline recording = RecordingPipeline.Create(scene);
        var binding = new PlaybackSensitiveLiveBinding(scene, recording.History) { IsPlaying = true };
        var source = new LiveSessionSource();
        source.Attach(binding);
        var manager = new AgentSessionManager();
        manager.UseSource(source);
        recording.History.ExecuteInTransaction(() => element.Start = TimeSpan.FromSeconds(2), "first");
        recording.History.ExecuteInTransaction(() => element.Start = TimeSpan.FromSeconds(3), "second");

        ToolResult<HistoryStateResponse> undo = await new HistoryTools(manager).Undo(steps: 2);

        Assert.Multiple(() =>
        {
            Assert.That(undo.IsSuccess, Is.True, undo.Error?.Message);
            Assert.That(undo.Value!.Applied, Has.Count.EqualTo(2));
            Assert.That(binding.GuardedInvocationCount, Is.EqualTo(1));
            Assert.That(binding.PauseCount, Is.EqualTo(1));
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
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

    private sealed class InterleavingLiveSession(AgentToolkitTestSession inner)
        : IEditingSession, IEditingSessionDispatcher
    {
        public Action? AfterNextInvoke { get; set; }

        public int InvokeCount { get; private set; }

        public string SessionId => inner.SessionId;

        public EditingSessionSource Source => EditingSessionSource.LiveEditor;

        public CoreObject Root => inner.Root;

        public Beutl.Editor.HistoryManager History => inner.History;

        public Beutl.AgentToolkit.Documents.DocumentAdapter Documents => inner.Documents;

        public bool IsDirty => inner.IsDirty;

        public void Invoke(Action action)
        {
            InvokeCount++;
            action();

            Action? after = AfterNextInvoke;
            AfterNextInvoke = null;
            after?.Invoke();
        }
    }

    private sealed class PlaybackSensitiveLiveBinding(Scene scene, HistoryManager history) : ILiveSessionBinding
    {
        public bool IsPlaying { get; set; }

        public int PauseCount { get; private set; }

        public int GuardedInvocationCount { get; private set; }

        public bool HistoryMutatedWhilePlaying { get; private set; }

        public bool PendingWorkFlushedWhilePlaying { get; private set; }

        public Scene? ActiveScene => scene;

        public HistoryManager? ActiveHistory => history;

        public bool IsAlive => true;

        public void Invoke(Action action)
        {
            int undoCount = history.UndoCount;
            int redoCount = history.RedoCount;
            action();
            if (IsPlaying && (history.UndoCount != undoCount || history.RedoCount != redoCount))
            {
                HistoryMutatedWhilePlaying = true;
            }
        }

        public ValueTask<TResult> ExecuteHistoryMutationAsync<TResult>(
            Func<HistoryManager, bool> shouldPause,
            Func<HistoryManager, TResult> operation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GuardedInvocationCount++;
            if (shouldPause(history))
            {
                PauseCount++;
                IsPlaying = false;
            }

            PendingWorkFlushedWhilePlaying |= IsPlaying && history.HasPendingOperations;
            history.FlushPendingMutations();
            int undoCount = history.UndoCount;
            int redoCount = history.RedoCount;
            TResult result = operation(history);
            if (IsPlaying && (history.UndoCount != undoCount || history.RedoCount != redoCount))
            {
                HistoryMutatedWhilePlaying = true;
            }

            return ValueTask.FromResult(result);
        }
    }
}
