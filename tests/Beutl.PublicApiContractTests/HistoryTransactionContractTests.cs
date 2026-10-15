using System.Collections.Specialized;
using System.Reactive.Linq;
using Beutl.Editor;
using Beutl.Editor.Operations;

namespace Beutl.PublicApiContractTests;

public sealed class HistoryTransactionContractTests
{
    [Test]
    public void Entries_can_be_subscribed_through_the_collection_interface()
    {
        using var manager = new HistoryManager(new OperationSequenceGenerator());
        var actions = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)manager.Entries).CollectionChanged += (_, args) => actions.Add(args.Action);

        manager.Record(() => { }, () => { });
        manager.Commit("Recorded");

        Assert.That(actions, Does.Contain(NotifyCollectionChangedAction.Add));
    }

    [Test]
    public void ExecuteInTransaction_CancellationDuringFlushLeavesPendingWorkUncommitted()
    {
        using var manager = new HistoryManager(new OperationSequenceGenerator());
        int value = 1;
        manager.Record(() => value = 1, () => value = 0);
        using var cancellation = new CancellationTokenSource();
        using IDisposable subscription = manager.BeforeMutation.Subscribe(_ => cancellation.Cancel());
        bool actionInvoked = false;

        Assert.Throws<OperationCanceledException>(() => manager.ExecuteInTransaction(
            () => actionInvoked = true,
            "Cancelled",
            cancellationToken: cancellation.Token));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(actionInvoked, Is.False);
            Assert.That(value, Is.EqualTo(1));
            Assert.That(manager.UndoCount, Is.Zero);
            Assert.That(manager.HasPendingOperations, Is.True);
        }
    }
}
