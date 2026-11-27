using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.Editor;
using Beutl.Editor.Components.AgentTab;
using Beutl.Editor.Operations;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AgentTurnHistoryTrackerTests
{
    [AvaloniaTest]
    public void RevertsContiguousAgentCommitsAndPreservesAnEarlierPendingUserEdit()
    {
        using var history = new HistoryManager(new OperationSequenceGenerator());
        var tracker = new AgentTurnHistoryTracker();
        var turn = tracker.TryBegin(Guid.NewGuid(), Guid.NewGuid(), history)!;
        int value = 0;
        Record(history, () => value, next => value = next, 1);
        turn.TrackEdit(history, () => history.ExecuteInTransaction(() => Record(history, () => value, next => value = next, 2), "Move title"));
        turn.TrackEdit(history, () => history.ExecuteInTransaction(() => Record(history, () => value, next => value = next, 3), "Shorten fade"));

        Assert.That(tracker.Finish(turn), Is.EqualTo(2));
        Assert.That(turn.Changes, Is.EqualTo(new[] { "Move title", "Shorten fade" }));
        Assert.That(tracker.CanRevert(history, turn.Id), Is.True);
        Assert.That(tracker.Revert(history, turn.Id), Is.EqualTo(AgentTurnRevertResult.Reverted));
        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo(1));
            Assert.That(history.UndoCount, Is.EqualTo(1));
            Assert.That(history.RedoCount, Is.EqualTo(2));
            Assert.That(tracker.CanRevert(history, turn.Id), Is.False);
        });
    }

    [AvaloniaTest]
    public void RejectsARevertIfUserAndAgentCommitsAreInterleaved()
    {
        using var history = new HistoryManager(new OperationSequenceGenerator());
        var tracker = new AgentTurnHistoryTracker();
        var turn = tracker.TryBegin(Guid.NewGuid(), Guid.NewGuid(), history)!;
        int value = 0;
        void SetValue(int next) => Record(history, () => value, result => value = result, next);
        turn.TrackEdit(history, () => history.ExecuteInTransaction(() => SetValue(1)));
        history.ExecuteInTransaction(() => SetValue(2));
        turn.TrackEdit(history, () => history.ExecuteInTransaction(() => SetValue(3)));

        Assert.That(tracker.Finish(turn), Is.EqualTo(2));
        Assert.That(tracker.CanRevert(history, turn.Id), Is.False);
        Assert.That(tracker.Revert(history, turn.Id), Is.EqualTo(AgentTurnRevertResult.HistoryChanged));
        Assert.That(value, Is.EqualTo(3));
        Assert.That(history.UndoCount, Is.EqualTo(3));
    }

    [AvaloniaTest]
    public void RechecksTheUndoStackAfterFlushingADebouncedUserEdit()
    {
        using var history = new HistoryManager(new OperationSequenceGenerator());
        var tracker = new AgentTurnHistoryTracker();
        var turn = tracker.TryBegin(Guid.NewGuid(), Guid.NewGuid(), history)!;
        int value = 0;
        turn.TrackEdit(history, () => history.ExecuteInTransaction(() => Record(history, () => value, next => value = next, 1)));
        tracker.Finish(turn);
        Assert.That(tracker.CanRevert(history, turn.Id), Is.True);

        using var flush = history.BeforeMutation.Subscribe(_ =>
        {
            Record(history, () => value, next => value = next, 2);
            history.Commit("User edit");
        });
        Assert.That(tracker.Revert(history, turn.Id), Is.EqualTo(AgentTurnRevertResult.HistoryChanged));
        Assert.That(value, Is.EqualTo(2));
        Assert.That(history.UndoCount, Is.EqualTo(2));
    }

    [AvaloniaTest]
    public async Task ARequestOnlyTracksTheMatchingThreadAndEditor()
    {
        using var history = new HistoryManager(new OperationSequenceGenerator());
        using var other = new HistoryManager(new OperationSequenceGenerator());
        var tracker = new AgentTurnHistoryTracker();
        var turn = tracker.TryBegin(Guid.NewGuid(), Guid.NewGuid(), history)!;
        int value = 0;
        await tracker.RunRequestAsync(Guid.NewGuid(), () =>
        {
            Assert.That(AgentTurnHistoryTracker.RequestTurn, Is.Null);
            return Task.CompletedTask;
        });
        await tracker.RunRequestAsync(turn.ThreadId, async () =>
        {
            await Task.Yield();
            var routed = AgentTurnHistoryTracker.RequestTurn;
            Assert.That(routed, Is.SameAs(turn));
            routed!.TrackEdit(other, () => other.ExecuteInTransaction(() => Record(other, () => value, next => value = next, 1)));
        });
        Assert.That(AgentTurnHistoryTracker.RequestTurn, Is.Null);
        Assert.That(tracker.Finish(turn), Is.Zero);
        Assert.That(tracker.Revert(history, turn.Id), Is.EqualTo(AgentTurnRevertResult.Unavailable));
    }

    private static void Record(HistoryManager history, Func<int> get, Action<int> set, int value)
    {
        int before = get();
        set(value);
        history.Record(() => set(value), () => set(before));
    }
}
