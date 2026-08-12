using Beutl.Editor;
using Beutl.Helpers;
using Beutl.ProjectSystem;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class FirstEditTelemetryTrackerTests
{
    [Test]
    public void OrdinaryCommitsReportFirstEditExactlyOnce()
    {
        using var history = new HistoryManager(new Scene(), new OperationSequenceGenerator());
        int recorded = 0;
        var tracker = new FirstEditTelemetryTracker(() => recorded++);
        using IDisposable subscription = history.SubscribeEntries(tracker.OnHistoryEntriesChanged).Subscription;

        history.Record(static () => { }, static () => { });
        history.Commit("first edit");
        history.Record(static () => { }, static () => { });
        history.Commit("second edit");

        Assert.That(recorded, Is.EqualTo(1));
    }

    [Test]
    public void UndoWithoutAnOrdinaryEditDoesNotReportFirstEdit()
    {
        using var history = new HistoryManager(new Scene(), new OperationSequenceGenerator());
        int recorded = 0;
        var tracker = new FirstEditTelemetryTracker(() => recorded++);
        using IDisposable subscription = history.SubscribeEntries(tracker.OnHistoryEntriesChanged).Subscription;

        bool changed = history.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(recorded, Is.Zero);
        });
    }
}
