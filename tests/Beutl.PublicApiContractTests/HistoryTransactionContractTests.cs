using System.Reactive.Linq;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.ProjectSystem;

namespace Beutl.PublicApiContractTests;

public sealed class HistoryTransactionContractTests : PublicApiContractTestBase
{
    [Test]
    public void Entries_framework_events_are_protected_and_public_subscription_uses_interfaces()
    {
        var type = typeof(System.Collections.ObjectModel.ReadOnlyObservableCollection<HistoryEntry>);
        foreach (string name in new[] { "CollectionChanged", "PropertyChanged" })
        {
            Assert.That(type.GetEvent(name), Is.Null);
            var hidden = type.GetEvent(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(hidden!.GetAddMethod(true)!.IsFamily, Is.True);
        }
    }

    [Test]
    public void ExecuteInTransaction_IsPublicAndCommitsPendingWorkSeparately()
    {
        AssertDoesNotHaveFriendAccess(typeof(HistoryManager).Assembly);
        using var manager = new HistoryManager(new Scene(), new OperationSequenceGenerator());
        int value = 1;
        manager.Record(() => value = 1, () => value = 0, "Pending");

        manager.ExecuteInTransaction(() =>
        {
            value = 2;
            manager.Record(() => value = 2, () => value = 1, "Isolated");
        }, "Isolated");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(2));
            Assert.That(manager.UndoCount, Is.EqualTo(2));
            Assert.That(manager.HasPendingOperations, Is.False);
        }
        Assert.That(manager.Undo(), Is.True);
        Assert.That(value, Is.EqualTo(1));
        Assert.That(manager.Undo(), Is.True);
        Assert.That(value, Is.Zero);
    }

    [Test]
    public void ExecuteInTransaction_RollsBackOnlyTheActionOnFailure()
    {
        AssertDoesNotHaveFriendAccess(typeof(HistoryManager).Assembly);
        using var manager = new HistoryManager(new Scene(), new OperationSequenceGenerator());
        int value = 1;
        manager.Record(() => value = 1, () => value = 0, "Pending");

        Assert.Throws<InvalidOperationException>(() => manager.ExecuteInTransaction(() =>
        {
            value = 2;
            manager.Record(() => value = 2, () => value = 1, "Isolated");
            throw new InvalidOperationException("Injected mutation failure");
        }, "Isolated"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(1));
            Assert.That(manager.UndoCount, Is.EqualTo(1));
            Assert.That(manager.HasPendingOperations, Is.False);
        }
    }

    [Test]
    public void ExecuteInTransaction_CancellationDuringFlushLeavesPendingWorkUncommitted()
    {
        AssertDoesNotHaveFriendAccess(typeof(HistoryManager).Assembly);
        using var manager = new HistoryManager(new Scene(), new OperationSequenceGenerator());
        int value = 1;
        manager.Record(() => value = 1, () => value = 0, "Pending");
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
