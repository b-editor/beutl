using System.Collections.Specialized;
using Beutl.Editor;
using Beutl.Editor.Operations;

namespace Beutl.UnitTests.Editor;

public class HistoryManagerTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void PartialHistoryFailure_RetryDoesNotRepeatSuccessfulOperations(bool redo)
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        void RecordAtomic(Action apply, Action revert) => manager.Record(new AtomicOperation(apply, revert)
        {
            SequenceNumber = _sequenceGenerator.GetNext(),
        });
        int value = 2;
        bool fail = true;
        void FailsOnce()
        {
            if (!fail) return;
            fail = false;
            throw new InvalidOperationException("retryable failure before mutation");
        }
        if (redo)
        {
            RecordAtomic(() => value++, () => value--);
            RecordAtomic(() => { FailsOnce(); value++; }, () => value--);
        }
        else
        {
            RecordAtomic(() => value++, () => { FailsOnce(); value--; });
            RecordAtomic(() => value++, () => value--);
        }
        manager.Commit("non-idempotent operations");
        if (redo) Assert.That(manager.Undo(), Is.True);
        Assert.Throws<InvalidOperationException>(() => { if (redo) manager.Redo(); else manager.Undo(); });
        Assert.That(value, Is.EqualTo(1));
        Assert.That(redo ? manager.Redo() : manager.Undo(), Is.True);
        Assert.That(value, Is.EqualTo(redo ? 2 : 0));
        Assert.That(redo ? manager.Undo() : manager.Redo(), Is.True);
        Assert.That(value, Is.EqualTo(redo ? 0 : 2));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void UnknownPartialFailure_PreservesHistoryAndAllowsReplayOfNewEdits(bool redo, bool jump)
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        CreateValueOperation(manager, 1, 0);
        manager.Commit("Earlier operation");
        HistoryTransaction earlier = manager.PeekUndo()!;
        var failure = new IOException("partial mutation");
        manager.Record(
            () => { _root.Value++; if (redo) throw failure; },
            () => { _root.Value--; if (!redo) throw failure; });
        _root.Value = 2;
        manager.Commit("unsafe operation");
        HistoryTransaction unsafeTransaction = manager.PeekUndo()!;
        CreateValueOperation(manager, 3, 2);
        manager.Commit("Later operation");
        HistoryTransaction later = manager.PeekUndo()!;
        HistoryEntry[] entries = manager.GetEntriesSnapshot();
        if (!jump)
        {
            manager.Undo();
            if (redo)
                manager.Undo();
        }
        else if (redo)
        {
            manager.JumpTo(0);
        }

        var states = new List<HistoryState>();
        using var subscription = manager.StateChanged.Subscribe(states.Add);
        var exception = Assert.Throws<HistoryReplayException>(() =>
        {
            if (jump) manager.JumpTo(redo ? 3 : 0);
            else if (redo) manager.Redo();
            else manager.Undo();
        });
        int survivingValue = redo ? 2 : 1;
        int boundary = redo ? 1 : 2;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.InnerException, Is.SameAs(failure));
            Assert.That(_root.Value, Is.EqualTo(survivingValue));
            Assert.That(manager.CurrentIndex, Is.EqualTo(boundary));
            Assert.That(manager.UndoCount, Is.EqualTo(boundary));
            Assert.That(manager.RedoCount, Is.EqualTo(3 - boundary));
            Assert.That(manager.PeekUndo(), Is.SameAs(redo ? earlier : unsafeTransaction));
            Assert.That(manager.PeekRedo(), Is.SameAs(redo ? unsafeTransaction : later));
            Assert.That(manager.HasPendingOperations, Is.False);
            Assert.That(manager.Entries, Is.EqualTo(entries));
            Assert.That(states, Is.EqualTo(new[] { new HistoryState(false, false, boundary, 3 - boundary) }));
        }
        Assert.DoesNotThrow(() => manager.Commit());
        Assert.DoesNotThrow(() => manager.Rollback());
        Assert.DoesNotThrow(() => manager.ExecuteInTransaction(() => { }));
        Assert.That(manager.Undo(), Is.False);
        Assert.That(manager.Redo(), Is.False);
        for (int index = 0; index < entries.Length; index++)
        {
            Assert.That(manager.WouldJumpToMove(index), Is.False);
            Assert.That(manager.JumpTo(index), Is.False);
        }
        Assert.That(_root.Value, Is.EqualTo(survivingValue));
        Assert.That(manager.Entries, Is.EqualTo(entries));

        CreateValueOperation(manager, 10, survivingValue);
        Assert.DoesNotThrow(() => manager.Commit("New edit"));
        Assert.That(states[^1], Is.EqualTo(new HistoryState(true, false, boundary + 1, 0)));
        Assert.That(manager.Entries.Take(boundary + 1), Is.EqualTo(entries.Take(boundary + 1)));
        Assert.That(manager.WouldJumpToMove(0), Is.False);
        Assert.That(manager.JumpTo(0), Is.False, "An unreachable jump must not undo even the safe new edit.");
        Assert.That(_root.Value, Is.EqualTo(10));
        Assert.That(manager.Undo(), Is.True);
        Assert.That(_root.Value, Is.EqualTo(survivingValue));
        Assert.That(manager.CanUndo, Is.False);
        Assert.That(manager.Undo(), Is.False);
        Assert.That(manager.Redo(), Is.True);
        Assert.That(_root.Value, Is.EqualTo(10));

        CreateValueOperation(manager, 20, 10);
        manager.Commit("Another edit");
        Assert.That(manager.WouldJumpToMove(boundary), Is.True);
        Assert.That(manager.JumpTo(boundary), Is.True);
        Assert.That(_root.Value, Is.EqualTo(survivingValue));
        Assert.That(manager.JumpTo(boundary + 2), Is.True);
        Assert.That(_root.Value, Is.EqualTo(20));
    }

    [TestCase("Rollback")]
    [TestCase("Undo")]
    [TestCase("Redo")]
    [TestCase("JumpTo")]
    public void UnknownPendingFailure_PreservesBothStacksAndNotifies(string action)
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        CreateValueOperation(manager, 1, 0);
        manager.Commit("Undo entry");
        CreateValueOperation(manager, 2, 1);
        manager.Commit("Redo entry");
        manager.Undo();
        HistoryEntry[] entries = manager.GetEntriesSnapshot();
        HistoryTransaction undo = manager.PeekUndo()!;
        HistoryTransaction redo = manager.PeekRedo()!;
        manager.Record(() => _root.Value++, () => { _root.Value--; throw new IOException("partial rollback"); });
        _root.Value++;

        var states = new List<HistoryState>();
        using var subscription = manager.StateChanged.Subscribe(states.Add);
        Assert.Throws<HistoryReplayException>(() =>
        {
            switch (action)
            {
                case "Rollback": manager.Rollback(); break;
                case "Undo": manager.Undo(); break;
                case "Redo": manager.Redo(); break;
                case "JumpTo": manager.JumpTo(0); break;
            }
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_root.Value, Is.EqualTo(1));
            Assert.That(manager.UndoCount, Is.EqualTo(1));
            Assert.That(manager.RedoCount, Is.EqualTo(1));
            Assert.That(manager.PeekUndo(), Is.SameAs(undo));
            Assert.That(manager.PeekRedo(), Is.SameAs(redo));
            Assert.That(manager.HasPendingOperations, Is.False);
            Assert.That(manager.Entries, Is.EqualTo(entries));
            Assert.That(states, Is.EqualTo(new[] { new HistoryState(false, false, 1, 1) }));
        }
        Assert.That(manager.Undo(), Is.False);
        Assert.That(manager.Redo(), Is.False);
        Assert.That(manager.JumpTo(0), Is.False);
        Assert.That(manager.JumpTo(2), Is.False);

        CreateValueOperation(manager, 10, 1);
        Assert.DoesNotThrow(() => manager.Commit("New edit"));
        Assert.That(manager.Undo(), Is.True);
        Assert.That(_root.Value, Is.EqualTo(1));
        Assert.That(manager.Undo(), Is.False);
        Assert.That(manager.Redo(), Is.True);
        Assert.That(_root.Value, Is.EqualTo(10));
    }

    [Test]
    public void UnknownFailure_BlockedJumpDoesNotDiscardPendingEdit()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(() => { }, () => throw new IOException("unknown undo failure"));
        manager.Commit("unsafe operation");
        Assert.Throws<HistoryReplayException>(() => manager.Undo());

        CreateValueOperation(manager, 10, 0);
        Assert.That(manager.WouldJumpToMove(0), Is.False);
        Assert.That(manager.JumpTo(0), Is.False);
        Assert.That(manager.HasPendingOperations, Is.True);
        Assert.That(_root.Value, Is.EqualTo(10));
        manager.Commit("Pending edit");
        Assert.That(manager.Undo(), Is.True);
        Assert.That(_root.Value, Is.Zero);
        Assert.That(manager.Undo(), Is.False);
    }

    [Test]
    public void UnknownFailure_SecondFailureAdvancesBoundaryAndExplicitClearResetsIt()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        for (int i = 0; i < 2; i++)
        {
            manager.Record(() => { }, () => throw new IOException("unknown undo failure"));
            manager.Commit("unsafe operation");
            Assert.Throws<HistoryReplayException>(() => manager.Undo());
            Assert.That(manager.CurrentIndex, Is.EqualTo(i + 1));
            Assert.That(manager.Entries, Has.Count.EqualTo(i + 2));
            Assert.That(manager.Undo(), Is.False);
        }

        CreateValueOperation(manager, 10, 0);
        manager.Commit("New edit");
        Assert.That(manager.JumpTo(1), Is.False);
        Assert.That(manager.JumpTo(2), Is.True);
        Assert.That(_root.Value, Is.Zero);
        manager.Clear();
        Assert.That(manager.Entries.Single().IsInitial, Is.True);
        Assert.That(manager.CurrentIndex, Is.Zero);
        CreateValueOperation(manager, 20, 0);
        manager.Commit("Edit after clear");
        Assert.That(manager.Undo(), Is.True);
        Assert.That(_root.Value, Is.Zero);
        Assert.That(manager.Redo(), Is.True);
        Assert.That(_root.Value, Is.EqualTo(20));
    }

    [Test]
    public void AtomicUndoFailureKeepsHistory()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(new AtomicOperation(() => { }, () => throw new InvalidOperationException("probe"))
        {
            SequenceNumber = _sequenceGenerator.GetNext(),
        });
        manager.Commit("probe");
        Assert.Throws<InvalidOperationException>(() => manager.Undo());
        Assert.That(manager.UndoCount, Is.EqualTo(1), "Failed undo must retain the transaction");
    }

    [Test]
    public void AtomicRedoFailureKeepsHistory()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(new AtomicOperation(() => throw new InvalidOperationException("probe"), () => { })
        {
            SequenceNumber = _sequenceGenerator.GetNext(),
        });
        manager.Commit("probe");
        manager.Undo();
        Assert.Throws<InvalidOperationException>(() => manager.Redo());
        Assert.That(manager.RedoCount, Is.EqualTo(1), "Failed redo must retain the transaction");
    }

    [Test]
    public void PartialAtomicUndoFailureNotifiesAndKeepsTheEntry()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        int value = 2;
        manager.Record(new AtomicOperation(() => { }, () => throw new InvalidOperationException("failure"))
        {
            SequenceNumber = _sequenceGenerator.GetNext(),
        });
        manager.Record(() => value = 2, () => value = 1);
        manager.Commit("partial");
        HistoryState? observed = null;
        using var subscription = manager.StateChanged.Subscribe(state => observed = state);
        Assert.Throws<InvalidOperationException>(() => manager.Undo());
        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo(1));
            Assert.That(manager.UndoCount, Is.EqualTo(1));
            Assert.That(manager.RedoCount, Is.Zero);
            Assert.That(manager.Entries.Count, Is.EqualTo(2));
            Assert.That(observed?.UndoCount, Is.EqualTo(1));
        });
    }

    private TestCoreObject _root = null!;
    private OperationSequenceGenerator _sequenceGenerator = null!;

    [SetUp]
    public void Setup()
    {
        _root = new TestCoreObject();
        _sequenceGenerator = new OperationSequenceGenerator();
    }

    [Test]
    public void Constructor_ShouldInitializeWithCorrectDefaults()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        Assert.Multiple(() =>
        {
            Assert.That(manager.Root, Is.EqualTo(_root));
            Assert.That(manager.CanUndo, Is.False);
            Assert.That(manager.CanRedo, Is.False);
            Assert.That(manager.UndoCount, Is.EqualTo(0));
            Assert.That(manager.RedoCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void Constructor_ShouldThrowArgumentNullException_WhenRootIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new HistoryManager(null!, _sequenceGenerator));
    }

    [Test]
    public void Constructor_ShouldThrowArgumentNullException_WhenSequenceGeneratorIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new HistoryManager(_root, null!));
    }

    [Test]
    public void Record_ShouldThrowArgumentNullException_WhenOperationIsNull()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        Assert.Throws<ArgumentNullException>(() => manager.Record((ChangeOperation)null!));
    }

    [Test]
    public void Commit_ShouldPushCurrentTransactionToUndoStack()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        var operation = CreateTestOperation();

        manager.Record(operation);
        manager.Commit("Test Operation");

        Assert.Multiple(() =>
        {
            Assert.That(manager.CanUndo, Is.True);
            Assert.That(manager.CanRedo, Is.False);
            Assert.That(manager.UndoCount, Is.EqualTo(1));
            Assert.That(manager.PeekUndo()?.DisplayName, Is.EqualTo("Test Operation"));
        });
    }

    [Test]
    public void Commit_ShouldNotPushEmptyTransaction()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        manager.Commit("Empty");

        Assert.Multiple(() =>
        {
            Assert.That(manager.CanUndo, Is.False);
            Assert.That(manager.UndoCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void Commit_ShouldClearRedoStack()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("First");
        manager.Undo();
        Assert.That(manager.CanRedo, Is.True);

        manager.Record(CreateTestOperation());
        manager.Commit("Second");

        Assert.That(manager.CanRedo, Is.False);
    }

    [Test]
    public void Rollback_ShouldRevertCurrentTransaction()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;

        var operation = CustomOperation.Create(
            () => _root.Value = 100,
            () => _root.Value = 0,
            _sequenceGenerator);

        manager.Record(operation);
        operation.Apply();
        Assert.That(_root.Value, Is.EqualTo(100));

        manager.Rollback();

        Assert.Multiple(() =>
        {
            Assert.That(_root.Value, Is.EqualTo(0));
            Assert.That(manager.CanUndo, Is.False);
        });
    }

    [Test]
    public void Rollback_WhenRevertFails_RetainsThePendingTransactionForRetry()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        bool failFirstRevert = true;
        var operation = new AtomicOperation(
            () => _root.Value = 100,
            () =>
            {
                if (failFirstRevert)
                {
                    failFirstRevert = false;
                    throw new InvalidOperationException("Injected revert failure");
                }
                _root.Value = 0;
            })
        { SequenceNumber = _sequenceGenerator.GetNext() };
        operation.Apply();
        manager.Record(operation);

        Assert.Throws<InvalidOperationException>(() => manager.Rollback());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_root.Value, Is.EqualTo(100));
            Assert.That(manager.HasPendingOperations, Is.True);
        }

        Assert.DoesNotThrow(() => manager.Rollback());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_root.Value, Is.Zero);
            Assert.That(manager.HasPendingOperations, Is.False);
        }
    }

    [Test]
    public void Undo_ShouldRevertLastCommittedTransaction()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;

        var operation = CustomOperation.Create(
            () => _root.Value = 100,
            () => _root.Value = 0,
            _sequenceGenerator);

        operation.Apply();
        manager.Record(operation);
        manager.Commit("Set Value");

        var result = manager.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(_root.Value, Is.EqualTo(0));
            Assert.That(manager.CanUndo, Is.False);
            Assert.That(manager.CanRedo, Is.True);
        });
    }

    [Test]
    public void Undo_ShouldReturnFalse_WhenNoUndoAvailable()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        var result = manager.Undo();

        Assert.That(result, Is.False);
    }

    [Test]
    public void Redo_ShouldReapplyLastUndoneTransaction()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;

        var operation = CustomOperation.Create(
            () => _root.Value = 100,
            () => _root.Value = 0,
            _sequenceGenerator);

        operation.Apply();
        manager.Record(operation);
        manager.Commit("Set Value");
        manager.Undo();

        var result = manager.Redo();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(_root.Value, Is.EqualTo(100));
            Assert.That(manager.CanUndo, Is.True);
            Assert.That(manager.CanRedo, Is.False);
        });
    }

    [Test]
    public void Redo_ShouldReturnFalse_WhenNoRedoAvailable()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        var result = manager.Redo();

        Assert.That(result, Is.False);
    }

    [Test]
    public void Clear_ShouldRemoveAllHistory()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("First");
        manager.Record(CreateTestOperation());
        manager.Commit("Second");
        manager.Undo();

        manager.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(manager.CanUndo, Is.False);
            Assert.That(manager.CanRedo, Is.False);
            Assert.That(manager.UndoCount, Is.EqualTo(0));
            Assert.That(manager.RedoCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void PeekUndo_ShouldReturnLastTransaction()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("First");
        manager.Record(CreateTestOperation());
        manager.Commit("Second");

        var transaction = manager.PeekUndo();

        Assert.That(transaction?.DisplayName, Is.EqualTo("Second"));
    }

    [Test]
    public void PeekUndo_ShouldReturnNull_WhenNoUndoAvailable()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        var transaction = manager.PeekUndo();

        Assert.That(transaction, Is.Null);
    }

    [Test]
    public void PeekRedo_ShouldReturnLastUndoneTransaction()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("First");
        manager.Undo();

        var transaction = manager.PeekRedo();

        Assert.That(transaction?.DisplayName, Is.EqualTo("First"));
    }

    [Test]
    public void PeekRedo_ShouldReturnNull_WhenNoRedoAvailable()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        var transaction = manager.PeekRedo();

        Assert.That(transaction, Is.Null);
    }

    [Test]
    public void ExecuteInTransaction_ShouldCommitOnSuccess()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;

        manager.ExecuteInTransaction(() =>
        {
            var operation = CustomOperation.Create(
                () => _root.Value = 100,
                () => _root.Value = 0,
                _sequenceGenerator);
            operation.Apply();
            manager.Record(operation);
        }, "Transaction");

        Assert.Multiple(() =>
        {
            Assert.That(manager.CanUndo, Is.True);
            Assert.That(manager.PeekUndo()?.DisplayName, Is.EqualTo("Transaction"));
        });
    }

    [Test]
    public void ExecuteInTransaction_ShouldRollbackOnException()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;

        Assert.Throws<InvalidOperationException>(() =>
        {
            manager.ExecuteInTransaction(() =>
            {
                var operation = CustomOperation.Create(
                    () => _root.Value = 100,
                    () => _root.Value = 0,
                    _sequenceGenerator);
                operation.Apply();
                manager.Record(operation);
                throw new InvalidOperationException("Test exception");
            }, "Transaction");
        });

        Assert.Multiple(() =>
        {
            Assert.That(_root.Value, Is.EqualTo(0));
            Assert.That(manager.CanUndo, Is.False);
        });
    }

    [Test]
    public void ExecuteInTransaction_CommitsPendingOperationsSeparately()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        CreateValueOperation(manager, targetValue: 1, previousValue: 0);

        manager.ExecuteInTransaction(
            () => CreateValueOperation(
                manager,
                targetValue: 2,
                previousValue: 1),
            "Isolated");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_root.Value, Is.EqualTo(2));
            Assert.That(manager.UndoCount, Is.EqualTo(2));
            Assert.That(manager.PeekUndo()?.DisplayName, Is.EqualTo("Isolated"));
            Assert.That(manager.HasPendingOperations, Is.False);
        }

        Assert.That(manager.Undo(), Is.True);
        Assert.That(_root.Value, Is.EqualTo(1));
        Assert.That(manager.Undo(), Is.True);
        Assert.That(_root.Value, Is.Zero);
    }

    [Test]
    public void ExecuteInTransaction_RollsBackOnlyItsOwnFailedOperations()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        CreateValueOperation(manager, targetValue: 1, previousValue: 0);

        Assert.Throws<InvalidOperationException>(() => manager.ExecuteInTransaction(() =>
        {
            CreateValueOperation(
                manager,
                targetValue: 2,
                previousValue: 1);
            throw new InvalidOperationException("isolated mutation failed");
        }, "Isolated"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_root.Value, Is.EqualTo(1));
            Assert.That(manager.UndoCount, Is.EqualTo(1));
            Assert.That(manager.HasPendingOperations, Is.False);
        }
        Assert.That(manager.Undo(), Is.True);
        Assert.That(_root.Value, Is.Zero);
    }

    [Test]
    public async Task ExecuteInTransaction_QueuesConcurrentRecordsForTheNextTransaction()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        using var isolatedEntered = new ManualResetEventSlim();
        using var releaseIsolated = new ManualResetEventSlim();
        using var concurrentStarted = new ManualResetEventSlim();
        using var concurrentReturned = new ManualResetEventSlim();
        Task isolated = Task.Run(() => manager.ExecuteInTransaction(() =>
        {
            manager.Record(CreateTestOperation());
            isolatedEntered.Set();
            if (!releaseIsolated.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The isolated transaction was not released.");
        }, "Isolated"));

        Assert.That(isolatedEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
        Task concurrent = Task.Run(() =>
        {
            concurrentStarted.Set();
            manager.Record(CreateTestOperation());
            concurrentReturned.Set();
        });
        try
        {
            Assert.That(concurrentStarted.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(concurrentReturned.Wait(TimeSpan.FromMilliseconds(200)), Is.False);
        }
        finally
        {
            releaseIsolated.Set();
        }

        await Task.WhenAll(isolated, concurrent).WaitAsync(TimeSpan.FromSeconds(5));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(manager.UndoCount, Is.EqualTo(1));
            Assert.That(manager.HasPendingOperations, Is.True);
        }
        manager.Commit("Concurrent");
        Assert.That(manager.UndoCount, Is.EqualTo(2));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ExecuteInTransaction_RejectsReentrantTransactionControl(bool commit)
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        Assert.Throws<InvalidOperationException>(() => manager.ExecuteInTransaction(() =>
        {
            CreateValueOperation(
                manager,
                targetValue: 1,
                previousValue: 0);
            if (commit)
                manager.Commit("Nested");
            else
                manager.Rollback();
        }, "Isolated"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_root.Value, Is.Zero);
            Assert.That(manager.UndoCount, Is.Zero);
            Assert.That(manager.HasPendingOperations, Is.False);
        }
    }

    [Test]
    public void ExecuteInTransaction_ContainsPostCommitStateObserverFailure()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        using IDisposable subscription = manager.StateChanged.Subscribe(_ =>
            throw new InvalidOperationException("observer failed"));
        HistoryState? laterState = null;
        using IDisposable laterSubscription = manager.StateChanged.Subscribe(state =>
            laterState = state);

        Assert.DoesNotThrow(() => manager.ExecuteInTransaction(
            () => CreateValueOperation(
                manager,
                targetValue: 1,
                previousValue: 0),
            "Isolated"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_root.Value, Is.EqualTo(1));
            Assert.That(manager.UndoCount, Is.EqualTo(1));
            Assert.That(manager.HasPendingOperations, Is.False);
            Assert.That(laterState?.UndoCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void ExecuteInTransaction_DetachesAFailedRollbackTransaction()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        var operation = CustomOperation.Create(
            () => _root.Value = 1,
            () => throw new InvalidOperationException("revert failed"),
            _sequenceGenerator);

        AggregateException? exception = Assert.Throws<AggregateException>(() =>
            manager.ExecuteInTransaction(() =>
            {
                operation.Apply();
                manager.Record(operation);
                throw new InvalidOperationException("mutation failed");
            }, "Isolated"));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.InnerExceptions.Select(item => item.Message),
                Is.EqualTo(new[] { "mutation failed", "revert failed" }));
            Assert.That(manager.UndoCount, Is.Zero);
            Assert.That(manager.HasPendingOperations, Is.False);
        });

        manager.Record(CreateTestOperation());
        manager.Commit("After failure");
        Assert.That(manager.UndoCount, Is.EqualTo(1));
    }

    [TestCase(ChangeOperationFailureState.Unknown)]
    [TestCase(ChangeOperationFailureState.Unchanged)]
    [TestCase(ChangeOperationFailureState.Completed)]
    public void ExecuteInTransaction_FailedRollbackRestrictsReplayUnlessCompleted(ChangeOperationFailureState failureState)
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        CreateValueOperation(manager, 1, 0);
        manager.Commit("Undo entry");
        CreateValueOperation(manager, 2, 1);
        manager.Commit("Redo entry");
        manager.Undo();
        HistoryEntry[] entries = manager.GetEntriesSnapshot();
        HistoryTransaction undo = manager.PeekUndo()!;
        HistoryTransaction redo = manager.PeekRedo()!;
        var states = new List<HistoryState>();
        using var subscription = manager.StateChanged.Subscribe(states.Add);
        var actionFailure = new IOException("action failed");
        var rollbackFailure = new IOException("rollback failed");
        int otherValue = 0;

        var exception = Assert.Throws<AggregateException>(() => manager.ExecuteInTransaction(() =>
        {
            otherValue = 1;
            manager.Record(() => otherValue = 1, () => otherValue = 0);
            _root.Value = 5;
            manager.Record(new FailingRollbackOperation(() =>
            {
                if (failureState == ChangeOperationFailureState.Unknown)
                    _root.Value = 4;
                else if (failureState == ChangeOperationFailureState.Completed)
                    _root.Value = 1;
                throw rollbackFailure;
            }, failureState)
            { SequenceNumber = _sequenceGenerator.GetNext() });
            throw actionFailure;
        }));

        bool completed = failureState == ChangeOperationFailureState.Completed;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.InnerExceptions, Is.EqualTo(new[] { actionFailure, rollbackFailure }));
            Assert.That(otherValue, Is.Zero, "Best-effort rollback must continue after a failed operation.");
            Assert.That(manager.HasPendingOperations, Is.False);
            Assert.That(manager.Entries, Is.EqualTo(entries));
            Assert.That(manager.PeekUndo(), Is.SameAs(undo));
            Assert.That(manager.PeekRedo(), Is.SameAs(redo));
            Assert.That(manager.CanUndo, Is.EqualTo(completed));
            Assert.That(manager.CanRedo, Is.EqualTo(completed));
        }

        if (completed)
        {
            Assert.That(_root.Value, Is.EqualTo(1));
            Assert.That(manager.Undo(), Is.True);
            Assert.That(_root.Value, Is.Zero);
            Assert.That(manager.Redo(), Is.True);
            Assert.That(_root.Value, Is.EqualTo(1));
            return;
        }

        Assert.That(states, Is.EqualTo(new[] { new HistoryState(false, false, 1, 1) }));
        Assert.That(manager.Undo(), Is.False);
        Assert.That(manager.Redo(), Is.False);
        Assert.That(manager.JumpTo(0), Is.False);
        Assert.That(manager.JumpTo(2), Is.False);
        int survivingValue = failureState == ChangeOperationFailureState.Unknown ? 4 : 5;
        Assert.That(_root.Value, Is.EqualTo(survivingValue));
        CreateValueOperation(manager, 10, survivingValue);
        manager.Commit("New edit");
        Assert.That(manager.Undo(), Is.True);
        Assert.That(_root.Value, Is.EqualTo(survivingValue));
        Assert.That(manager.Undo(), Is.False);
        Assert.That(manager.Redo(), Is.True);
        Assert.That(_root.Value, Is.EqualTo(10));
    }

    private sealed class FailingRollbackOperation(Action revert, ChangeOperationFailureState failureState) : ChangeOperation
    {
        public override ChangeOperationFailureState FailureState => failureState;

        public override void Apply() => throw new NotSupportedException();

        public override void Revert() => revert();
    }

    // Leaves the state unchanged whenever its delegates throw, so history may retry it.
    private sealed class AtomicOperation(Action apply, Action revert) : ChangeOperation
    {
        public override ChangeOperationFailureState FailureState => ChangeOperationFailureState.Unchanged;

        public override void Apply() => apply();

        public override void Revert() => revert();
    }

    [Test]
    public void ExecuteInTransaction_ContainsHistoryEntryObserverFailure()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        int laterSubscriberNotifications = 0;
        INotifyCollectionChanged entries = manager.Entries;
        NotifyCollectionChangedEventHandler handler = (_, _) =>
            throw new InvalidOperationException("entry observer failed");
        entries.CollectionChanged += handler;
        IDisposable throwingSubscription = manager.SubscribeEntries((_, _) =>
            throw new InvalidOperationException("managed entry observer failed")).Subscription;
        IDisposable laterSubscription = manager.SubscribeEntries((_, _) =>
            laterSubscriberNotifications++).Subscription;
        try
        {
            Assert.DoesNotThrow(() => manager.ExecuteInTransaction(
                () => CreateValueOperation(
                    manager,
                    targetValue: 1,
                    previousValue: 0),
                "Isolated"));
        }
        finally
        {
            laterSubscription.Dispose();
            throwingSubscription.Dispose();
            entries.CollectionChanged -= handler;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_root.Value, Is.EqualTo(1));
            Assert.That(manager.UndoCount, Is.EqualTo(1));
            Assert.That(manager.Entries, Has.Count.EqualTo(2));
            Assert.That(manager.HasPendingOperations, Is.False);
            Assert.That(laterSubscriberNotifications, Is.EqualTo(1));
        }
    }

    [Test]
    public void Commit_RejectsDirectHistoryControlReentrancyBeforeManagedPublication()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        var mirroredEntries = manager.GetEntriesSnapshot().ToList();
        using IDisposable managedSubscription = manager.SubscribeEntries((_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems is not null)
            {
                mirroredEntries.Insert(
                    args.NewStartingIndex,
                    (HistoryEntry)args.NewItems[0]!);
            }
        }).Subscription;
        int reentrantAttempts = 0;
        INotifyCollectionChanged entries = manager.Entries;
        NotifyCollectionChangedEventHandler directHandler = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add)
            {
                reentrantAttempts++;
                manager.Clear();
            }
        };
        entries.CollectionChanged += directHandler;
        try
        {
            manager.Record(CreateTestOperation());
            Assert.DoesNotThrow(() => manager.Commit("Commit"));
        }
        finally
        {
            entries.CollectionChanged -= directHandler;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reentrantAttempts, Is.EqualTo(1));
            Assert.That(manager.UndoCount, Is.EqualTo(1));
            Assert.That(manager.Entries, Has.Count.EqualTo(2));
            Assert.That(
                mirroredEntries.Select(entry => entry.TransactionId),
                Is.EqualTo(manager.Entries.Select(entry => entry.TransactionId)));
        }
    }

    [Test]
    public void Commit_DoesNotReplayAddToManagedSubscriberCreatedDuringDirectPublication()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        List<HistoryEntry>? lateMirror = null;
        IDisposable? lateSubscription = null;
        INotifyCollectionChanged entries = manager.Entries;
        NotifyCollectionChangedEventHandler directHandler = (_, args) =>
        {
            if (args.Action != NotifyCollectionChangedAction.Add || lateSubscription is not null)
                return;

            var subscriptionResult = manager.SubscribeEntries((_, change) =>
            {
                if (change.Action == NotifyCollectionChangedAction.Add && change.NewItems is not null)
                {
                    lateMirror!.Insert(
                        change.NewStartingIndex,
                        (HistoryEntry)change.NewItems[0]!);
                }
            });
            lateSubscription = subscriptionResult.Subscription;
            lateMirror = subscriptionResult.InitialSnapshot.ToList();
        };
        entries.CollectionChanged += directHandler;
        try
        {
            manager.Record(CreateTestOperation());
            manager.Commit("Commit");
        }
        finally
        {
            entries.CollectionChanged -= directHandler;
            lateSubscription?.Dispose();
        }

        Assert.That(lateMirror, Is.Not.Null);
        Assert.That(
            lateMirror!.Select(entry => entry.TransactionId),
            Is.EqualTo(manager.Entries.Select(entry => entry.TransactionId)));
    }

    [Test]
    public void ExecuteInTransaction_GuardsPendingEntryPublication()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        CreateValueOperation(manager, targetValue: 1, previousValue: 0);
        int reentrantAttempts = 0;
        INotifyCollectionChanged entries = manager.Entries;
        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add)
            {
                reentrantAttempts++;
                manager.Rollback();
            }
        };
        entries.CollectionChanged += handler;
        try
        {
            Assert.DoesNotThrow(() => manager.ExecuteInTransaction(
                () => CreateValueOperation(
                    manager,
                    targetValue: 2,
                    previousValue: 1),
                "Isolated"));
        }
        finally
        {
            entries.CollectionChanged -= handler;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reentrantAttempts, Is.EqualTo(2));
            Assert.That(_root.Value, Is.EqualTo(2));
            Assert.That(manager.UndoCount, Is.EqualTo(2));
            Assert.That(manager.Entries, Has.Count.EqualTo(3));
            Assert.That(manager.HasPendingOperations, Is.False);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExecuteInTransaction_SeparatesRecordsProducedByPendingEntryPublication(
        bool useAtomicSubscription)
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        CreateValueOperation(manager, targetValue: 1, previousValue: 0);
        bool injected = false;
        INotifyCollectionChanged entries = manager.Entries;
        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add && !injected)
            {
                injected = true;
                CreateValueOperation(
                    manager,
                    targetValue: 2,
                    previousValue: 1);
            }
        };
        IDisposable? subscription = null;
        if (useAtomicSubscription)
            subscription = manager.SubscribeEntries(handler).Subscription;
        else
            entries.CollectionChanged += handler;
        try
        {
            manager.ExecuteInTransaction(() => CreateValueOperation(
                manager,
                targetValue: 3,
                previousValue: 2), "Callback");
        }
        finally
        {
            subscription?.Dispose();
            if (!useAtomicSubscription)
                entries.CollectionChanged -= handler;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_root.Value, Is.EqualTo(3));
            Assert.That(manager.UndoCount, Is.EqualTo(3));
            Assert.That(manager.HasPendingOperations, Is.False);
        }
        Assert.That(manager.Undo(), Is.True);
        Assert.That(_root.Value, Is.EqualTo(2));
        Assert.That(manager.Undo(), Is.True);
        Assert.That(_root.Value, Is.EqualTo(1));
        Assert.That(manager.Undo(), Is.True);
        Assert.That(_root.Value, Is.Zero);
    }

    [Test]
    public void ExecuteInTransaction_RejectsNonConvergingEntryPublicationBeforeCallback()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        bool actionInvoked = false;
        INotifyCollectionChanged entries = manager.Entries;
        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add)
                manager.Record(CreateTestOperation());
        };
        entries.CollectionChanged += handler;
        try
        {
            Assert.Throws<InvalidOperationException>(() => manager.ExecuteInTransaction(
                () => actionInvoked = true,
                "Callback"));
        }
        finally
        {
            entries.CollectionChanged -= handler;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(actionInvoked, Is.False);
            Assert.That(manager.UndoCount, Is.GreaterThan(1));
            Assert.That(manager.HasPendingOperations, Is.True);
        }
    }

    [Test]
    public void ExecuteInTransaction_ContinuesRollbackAfterARevertFailure()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        int firstValue = 0;
        int secondValue = 0;

        AggregateException? exception = Assert.Throws<AggregateException>(() =>
            manager.ExecuteInTransaction(() =>
            {
                var first = CustomOperation.Create(
                    () => firstValue = 1,
                    () => firstValue = 0,
                    _sequenceGenerator);
                first.Apply();
                manager.Record(first);

                var second = CustomOperation.Create(
                    () => secondValue = 1,
                    () => throw new InvalidOperationException("second revert failed"),
                    _sequenceGenerator);
                second.Apply();
                manager.Record(second);
                throw new InvalidOperationException("mutation failed");
            }, "Isolated"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                exception!.InnerExceptions.Select(item => item.Message),
                Is.EqualTo(new[] { "mutation failed", "second revert failed" }));
            Assert.That(firstValue, Is.Zero);
            Assert.That(secondValue, Is.EqualTo(1));
            Assert.That(manager.UndoCount, Is.Zero);
            Assert.That(manager.HasPendingOperations, Is.False);
        }
    }

    [Test]
    public void Record_WithActions_ShouldCreateCustomOperation()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;
        bool doExecuted = false;

        manager.Record(
            () => { doExecuted = true; _root.Value = 100; },
            () => { _root.Value = 0; });
        manager.Commit("Test");

        Assert.Multiple(() =>
        {
            Assert.That(doExecuted, Is.False);
            Assert.That(manager.CanUndo, Is.True);
        });
    }

    [TestCase("Commit", true, false, 1, 0)]
    [TestCase("Undo", false, true, 0, 1)]
    [TestCase("Redo", true, false, 1, 0)]
    [TestCase("Clear", false, false, 0, 0)]
    public void StateChanged_ShouldBeNotified(string operation, bool canUndo, bool canRedo, int undoCount, int redoCount)
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        if (operation != "Commit")
        {
            manager.Record(CreateTestOperation());
            manager.Commit("Test");
        }

        if (operation == "Redo")
            manager.Undo();

        HistoryState? receivedState = null;
        using var subscription = manager.StateChanged.Subscribe(state => receivedState = state);

        switch (operation)
        {
            case "Commit":
                manager.Record(CreateTestOperation());
                manager.Commit("Test");
                break;
            case "Undo":
                manager.Undo();
                break;
            case "Redo":
                manager.Redo();
                break;
            case "Clear":
                manager.Clear();
                break;
        }

        Assert.That(receivedState, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(receivedState!.Value.CanUndo, Is.EqualTo(canUndo));
            Assert.That(receivedState!.Value.CanRedo, Is.EqualTo(canRedo));
            Assert.That(receivedState!.Value.UndoCount, Is.EqualTo(undoCount));
            Assert.That(receivedState!.Value.RedoCount, Is.EqualTo(redoCount));
        });
    }

    [Test]
    public void StateChanged_DisposingOneOfTwoEqualObserversRemovesOnlyItsSubscription()
    {
        var manager = new HistoryManager(_root, _sequenceGenerator);
        var first = new EqualHistoryObserver();
        var second = new EqualHistoryObserver();
        using IDisposable firstSubscription = manager.StateChanged.Subscribe(first);
        IDisposable secondSubscription = manager.StateChanged.Subscribe(second);
        secondSubscription.Dispose();

        manager.Record(CreateTestOperation());
        manager.Commit("Test");
        manager.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.NotificationCount, Is.EqualTo(1));
            Assert.That(first.CompletionCount, Is.EqualTo(1));
            Assert.That(second.NotificationCount, Is.Zero);
            Assert.That(second.CompletionCount, Is.Zero);
        }
    }

    [Test]
    public void Dispose_ShouldPreventFurtherOperations()
    {
        var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Dispose();

        Assert.Throws<ObjectDisposedException>(() => manager.Record(CreateTestOperation()));
        Assert.Throws<ObjectDisposedException>(() => manager.Commit("Test"));
        Assert.Throws<ObjectDisposedException>(() => manager.Undo());
        Assert.Throws<ObjectDisposedException>(() => manager.Redo());
        Assert.Throws<ObjectDisposedException>(() => manager.Clear());
        Assert.Throws<ObjectDisposedException>(() =>
            manager.ExecuteInTransaction(static () => { }, "Test"));
        Assert.Throws<ObjectDisposedException>(() => manager.JumpTo(0));
        Assert.Throws<ObjectDisposedException>(() => manager.GetEntriesSnapshot());
        Assert.Throws<ObjectDisposedException>(() =>
            manager.SubscribeEntries((_, _) => { }));
        Assert.Throws<ObjectDisposedException>(() => manager.WouldJumpToMove(0));
        Assert.Throws<ObjectDisposedException>(() => _ = manager.HasPendingOperations);
        Assert.Throws<ObjectDisposedException>(() => manager.FlushPendingMutations());
    }

    [Test]
    public void MultipleUndoRedo_ShouldMaintainCorrectState()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;

        for (int i = 1; i <= 3; i++)
        {
            int value = i * 100;
            int prevValue = (i - 1) * 100;
            var operation = CustomOperation.Create(
                () => _root.Value = value,
                () => _root.Value = prevValue,
                _sequenceGenerator);
            operation.Apply();
            manager.Record(operation);
            manager.Commit($"Step {i}");
        }

        Assert.That(_root.Value, Is.EqualTo(300));
        Assert.That(manager.UndoCount, Is.EqualTo(3));

        manager.Undo();
        Assert.That(_root.Value, Is.EqualTo(200));

        manager.Undo();
        Assert.That(_root.Value, Is.EqualTo(100));

        manager.Redo();
        Assert.That(_root.Value, Is.EqualTo(200));

        manager.Undo();
        manager.Undo();
        Assert.That(_root.Value, Is.EqualTo(0));
        Assert.That(manager.CanUndo, Is.False);
        Assert.That(manager.RedoCount, Is.EqualTo(3));
    }

    private CustomOperation CreateTestOperation()
    {
        return CustomOperation.Create(
            () => { },
            () => { },
            _sequenceGenerator);
    }

    private CustomOperation CreateValueOperation(HistoryManager manager, int targetValue, int previousValue)
    {
        var op = CustomOperation.Create(
            () => _root.Value = targetValue,
            () => _root.Value = previousValue,
            _sequenceGenerator);
        op.Apply();
        manager.Record(op);
        return op;
    }

    private class TestCoreObject : CoreObject
    {
        public int Value { get; set; }
    }

    #region JumpTo / Entries Tests

    [Test]
    public void Entries_AfterConstruction_ShouldContainSingleInitialEntry()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        Assert.Multiple(() =>
        {
            Assert.That(manager.Entries.Count, Is.EqualTo(1));
            Assert.That(manager.Entries[0].IsInitial, Is.True);
            Assert.That(manager.CurrentIndex, Is.EqualTo(0));
        });
    }

    [Test]
    public void Entries_AfterCommit_ShouldAppendTransactionEntry()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;
        CreateValueOperation(manager, 100, 0);
        manager.Commit("First");

        Assert.Multiple(() =>
        {
            Assert.That(manager.Entries.Count, Is.EqualTo(2));
            Assert.That(manager.Entries[0].IsInitial, Is.True);
            Assert.That(manager.Entries[1].IsInitial, Is.False);
            Assert.That(manager.Entries[1].DisplayName, Is.EqualTo("First"));
            Assert.That(manager.CurrentIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void Commit_AfterUndo_ShouldTruncateEntriesAndKeepInitial()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;
        CreateValueOperation(manager, 100, 0);
        manager.Commit("Step1");
        CreateValueOperation(manager, 200, 100);
        manager.Commit("Step2");

        manager.Undo();
        Assert.That(manager.Entries.Count, Is.EqualTo(3));

        CreateValueOperation(manager, 50, 100);
        manager.Commit("Replacement");

        Assert.Multiple(() =>
        {
            Assert.That(manager.Entries.Count, Is.EqualTo(3));
            Assert.That(manager.Entries[0].IsInitial, Is.True);
            Assert.That(manager.Entries[1].DisplayName, Is.EqualTo("Step1"));
            Assert.That(manager.Entries[2].DisplayName, Is.EqualTo("Replacement"));
        });
    }

    [Test]
    public void Clear_ShouldLeaveSingleInitialEntry()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        CreateValueOperation(manager, 100, 0);
        manager.Commit("Step1");
        CreateValueOperation(manager, 200, 100);
        manager.Commit("Step2");

        manager.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(manager.Entries.Count, Is.EqualTo(1));
            Assert.That(manager.Entries[0].IsInitial, Is.True);
            Assert.That(manager.CurrentIndex, Is.EqualTo(0));
        });
    }

    [Test]
    public void JumpTo_OutOfRangeIndex_ShouldReturnFalseWithoutMutation()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;
        CreateValueOperation(manager, 100, 0);
        manager.Commit("Step1");

        int countBefore = manager.UndoCount;
        bool moved1 = manager.JumpTo(-1);
        bool moved2 = manager.JumpTo(manager.Entries.Count);

        Assert.Multiple(() =>
        {
            Assert.That(moved1, Is.False);
            Assert.That(moved2, Is.False);
            Assert.That(manager.UndoCount, Is.EqualTo(countBefore));
            Assert.That(_root.Value, Is.EqualTo(100));
        });
    }

    [Test]
    public void JumpTo_ToCurrentIndex_ShouldReturnFalseAndNotNotify()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        CreateValueOperation(manager, 100, 0);
        manager.Commit("Step1");

        HistoryState? receivedState = null;
        using var sub = manager.StateChanged.Subscribe(s => receivedState = s);

        bool moved = manager.JumpTo(manager.CurrentIndex);

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.False);
            Assert.That(receivedState, Is.Null);
        });
    }

    [Test]
    public void JumpTo_ToPastIndex_ShouldRevertInLifoOrder()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;
        CreateValueOperation(manager, 100, 0);
        manager.Commit("Step1");
        CreateValueOperation(manager, 200, 100);
        manager.Commit("Step2");
        CreateValueOperation(manager, 300, 200);
        manager.Commit("Step3");

        bool moved = manager.JumpTo(0);

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True);
            Assert.That(_root.Value, Is.EqualTo(0));
            Assert.That(manager.UndoCount, Is.EqualTo(0));
            Assert.That(manager.RedoCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void JumpTo_ToFutureIndex_ShouldReapplyInOrder()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;
        CreateValueOperation(manager, 100, 0);
        manager.Commit("Step1");
        CreateValueOperation(manager, 200, 100);
        manager.Commit("Step2");
        CreateValueOperation(manager, 300, 200);
        manager.Commit("Step3");

        manager.JumpTo(0);
        Assert.That(_root.Value, Is.EqualTo(0));

        bool moved = manager.JumpTo(2);

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True);
            Assert.That(_root.Value, Is.EqualTo(200));
            Assert.That(manager.UndoCount, Is.EqualTo(2));
            Assert.That(manager.RedoCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void JumpTo_WithUncommittedOperations_ShouldRollbackBeforeJumping()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;
        CreateValueOperation(manager, 100, 0);
        manager.Commit("Committed");

        var pending = CustomOperation.Create(
            () => _root.Value = 999,
            () => _root.Value = 100,
            _sequenceGenerator);
        pending.Apply();
        manager.Record(pending);

        bool moved = manager.JumpTo(0);

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True);
            Assert.That(_root.Value, Is.EqualTo(0));
            // The uncommitted operation must not survive into a future commit.
            CreateValueOperation(manager, 50, 0);
            manager.Commit("AfterJump");
            Assert.That(manager.PeekUndo()?.OperationCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void JumpTo_ToInitialIndex_ShouldRestoreInitialState()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;
        CreateValueOperation(manager, 100, 0);
        manager.Commit("Step1");

        bool moved = manager.JumpTo(0);

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True);
            Assert.That(manager.CurrentIndex, Is.EqualTo(0));
            Assert.That(manager.CanUndo, Is.False);
            Assert.That(manager.CanRedo, Is.True);
            Assert.That(_root.Value, Is.EqualTo(0));
        });
    }

    #endregion

    #region HistoryEntry Tests

    [Test]
    public void HistoryEntry_Initial_ShouldUseInitialLabel()
    {
        var entry = HistoryEntry.CreateInitial();

        Assert.Multiple(() =>
        {
            Assert.That(entry.IsInitial, Is.True);
            Assert.That(entry.TransactionId, Is.Null);
            Assert.That(entry.DisplayLabel, Is.EqualTo(Beutl.Language.Strings.History_Initial));
            Assert.That(entry.TransactionLabel, Is.EqualTo("•"));
        });
    }

    [Test]
    public void HistoryEntry_FromTransaction_DisplayLabelPrefersDisplayName()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("Friendly");

        HistoryEntry entry = manager.Entries[1];
        Assert.That(entry.DisplayLabel, Is.EqualTo("Friendly"));
    }

    [Test]
    public void HistoryEntry_FromTransaction_FallsBackToUnnamedWhenAllNull()
    {
        // Commit with a literal null name -> CallerArgumentExpression captures "(string?)null"
        // which is non-null, so we directly construct the entry to test the fallback path.
        var transaction = new HistoryTransaction(42);
        var entry = HistoryEntry.FromTransaction(transaction);

        Assert.Multiple(() =>
        {
            Assert.That(entry.IsInitial, Is.False);
            Assert.That(entry.DisplayLabel, Is.EqualTo(Beutl.Language.Strings.History_Unnamed));
            Assert.That(entry.TransactionLabel, Is.EqualTo("42"));
        });
    }

    #endregion

    #region JumpTo Failure / Disposal Tests

    [Test]
    public void JumpTo_WhenApplyThrowsMidForwardLoop_ShouldKeepFailingTransactionOnRedoStack()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;

        CreateValueOperation(manager, 100, 0);
        manager.Commit("Step1");
        CreateValueOperation(manager, 200, 100);
        manager.Commit("Step2");

        // Faulty's first Apply (the explicit pre-record call) succeeds; any
        // subsequent Apply (e.g. via Redo / JumpTo forward) throws.
        bool firstApply = true;
        var faulty = new AtomicOperation(
            () =>
            {
                if (firstApply)
                {
                    firstApply = false;
                    _root.Value = 300;
                    return;
                }
                throw new InvalidOperationException("Boom");
            },
            () => _root.Value = 200)
        { SequenceNumber = _sequenceGenerator.GetNext() };
        faulty.Apply();
        manager.Record(faulty);
        manager.Commit("Faulty");

        manager.JumpTo(0);
        // After jumping back: undo=[], redo=[Step1, Step2, Faulty(top)]
        Assert.That(manager.RedoCount, Is.EqualTo(3));

        HistoryState? observed = null;
        using var sub = manager.StateChanged.Subscribe(s => observed = s);

        Assert.Throws<InvalidOperationException>(() => manager.JumpTo(3));

        Assert.Multiple(() =>
        {
            // peek-then-pop keeps the throwing transaction on redo while the
            // ones that already applied move to undo.
            Assert.That(manager.UndoCount, Is.EqualTo(2));
            Assert.That(manager.RedoCount, Is.EqualTo(1));
            Assert.That(manager.PeekRedo()?.DisplayName, Is.EqualTo("Faulty"));
            // Successful steps must still emit a state-change notification.
            Assert.That(observed, Is.Not.Null);
        });
    }

    [Test]
    public void JumpTo_WhenRevertThrowsMidLoop_ShouldNotifyForCompletedStepsAndRethrow()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;

        // _undoStack (bottom -> top): Faulty, Step2, Step3
        // JumpTo(0) reverts Step3 (ok) -> Step2 (ok) -> Faulty (throws).
        var faulty = new AtomicOperation(
            () => _root.Value = 100,
            () => throw new InvalidOperationException("Boom"))
        { SequenceNumber = _sequenceGenerator.GetNext() };
        faulty.Apply();
        manager.Record(faulty);
        manager.Commit("Faulty");

        CreateValueOperation(manager, 200, 100);
        manager.Commit("Step2");
        CreateValueOperation(manager, 300, 200);
        manager.Commit("Step3");

        HistoryState? observed = null;
        using var sub = manager.StateChanged.Subscribe(s => observed = s);

        Assert.Throws<InvalidOperationException>(() => manager.JumpTo(0));

        Assert.Multiple(() =>
        {
            // Successful steps must still notify.
            Assert.That(observed, Is.Not.Null);
            // Stack integrity preserved by peek-then-pop: Step2/Step3 moved to redo,
            // Faulty stays on undo.
            Assert.That(manager.UndoCount, Is.EqualTo(1));
            Assert.That(manager.RedoCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void JumpTo_WhenPendingRevertThrows_ShouldStillReplaceCurrentTransaction()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;
        CreateValueOperation(manager, 100, 0);
        manager.Commit("Committed");

        // Pending op whose revert throws; it must not survive into the next commit.
        // DiscardPendingTransaction_NoLock replaces the pending transaction even when its revert throws.
        var pending = CustomOperation.Create(
            () => _root.Value = 999,
            () => throw new InvalidOperationException("PendingRevertFailed"),
            _sequenceGenerator);
        pending.Apply();
        manager.Record(pending);

        Assert.Throws<HistoryReplayException>(() => manager.JumpTo(0));

        CreateValueOperation(manager, 50, 999);
        manager.Commit("After");

        Assert.That(manager.PeekUndo()?.OperationCount, Is.EqualTo(1));
    }

    [Test]
    public void JumpTo_RejumpForwardAfterCommitCollapse_ShouldFail()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;
        CreateValueOperation(manager, 100, 0);
        manager.Commit("Step1");
        CreateValueOperation(manager, 200, 100);
        manager.Commit("Step2");
        CreateValueOperation(manager, 300, 200);
        manager.Commit("Step3");

        // Jump back, then commit -> redo stack collapses, entries truncate.
        manager.JumpTo(1);
        CreateValueOperation(manager, 50, 100);
        manager.Commit("Replacement");

        int previousValue = _root.Value;
        bool moved = manager.JumpTo(3); // No longer reachable.

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.False);
            Assert.That(_root.Value, Is.EqualTo(previousValue));
            Assert.That(manager.Entries.Count, Is.EqualTo(3));
        });
    }

    #endregion

    #region Snapshot Tests

    [Test]
    public void GetEntriesSnapshot_ReturnsLockSafeCopy()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        CreateValueOperation(manager, 100, 0);
        manager.Commit("Step1");

        HistoryEntry[] snapshot = manager.GetEntriesSnapshot();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Length, Is.EqualTo(2));
            Assert.That(snapshot[0].IsInitial, Is.True);
            Assert.That(snapshot[1].DisplayName, Is.EqualTo("Step1"));
        });

        // Mutating the manager must not affect the snapshot reference.
        CreateValueOperation(manager, 200, 100);
        manager.Commit("Step2");
        Assert.That(snapshot.Length, Is.EqualTo(2));
        Assert.That(manager.Entries.Count, Is.EqualTo(3));
    }

    [Test]
    public void SubscribeEntries_ReturnsCurrentSnapshotAndForwardsLaterChanges()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        CreateValueOperation(manager, 100, 0);
        manager.Commit("Step1");

        var received = new List<NotifyCollectionChangedAction>();
        var (sub, snapshot, currentIndex) = manager.SubscribeEntries((_, e) => received.Add(e.Action));
        using (sub)
        {
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.Length, Is.EqualTo(2));
                Assert.That(snapshot[0].IsInitial, Is.True);
                Assert.That(snapshot[1].DisplayName, Is.EqualTo("Step1"));
                Assert.That(currentIndex, Is.EqualTo(1));
            });

            CreateValueOperation(manager, 200, 100);
            manager.Commit("Step2");

            Assert.That(received, Does.Contain(NotifyCollectionChangedAction.Add));
        }
    }

    [Test]
    public void Direct_entry_subscribers_continue_after_collection_and_property_failures()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        var collection = (INotifyCollectionChanged)manager.Entries;
        var properties = (System.ComponentModel.INotifyPropertyChanged)manager.Entries;
        var actions = new List<NotifyCollectionChangedAction>();
        int propertyCalls = 0;
        collection.CollectionChanged += (_, _) => throw new InvalidOperationException("collection observer");
        collection.CollectionChanged += (_, args) => actions.Add(args.Action);
        properties.PropertyChanged += (_, _) => throw new InvalidOperationException("property observer");
        properties.PropertyChanged += (_, _) => propertyCalls++;
        manager.Record(CreateTestOperation());
        manager.Commit("first");
        manager.Record(CreateTestOperation());
        manager.Commit("second");
        manager.Undo();
        manager.Record(CreateTestOperation());
        manager.Commit("third");
        manager.Clear();
        Assert.That(actions, Does.Contain(NotifyCollectionChangedAction.Add));
        Assert.That(actions, Does.Contain(NotifyCollectionChangedAction.Replace));
        Assert.That(actions, Does.Contain(NotifyCollectionChangedAction.Remove));
        Assert.That(propertyCalls, Is.GreaterThan(0));
    }

    [Test]
    public void SubscribeEntries_DisposalUnsubscribesHandler()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        int callCount = 0;
        var (sub, _, _) = manager.SubscribeEntries((_, _) => callCount++);

        sub.Dispose();

        CreateValueOperation(manager, 1, 0);
        manager.Commit("AfterUnsubscribe");

        Assert.That(callCount, Is.EqualTo(0));
    }

    [Test]
    public void SubscribeEntries_NullHandler_ShouldThrow()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        Assert.Throws<ArgumentNullException>(() =>
            manager.SubscribeEntries(null!));
    }

    [Test]
    public void Clear_DoesNotEmitResetEvent()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        CreateValueOperation(manager, 100, 0);
        manager.Commit("Step1");
        CreateValueOperation(manager, 200, 100);
        manager.Commit("Step2");

        var actions = new List<NotifyCollectionChangedAction>();
        var (sub, _, _) = manager.SubscribeEntries((_, e) => actions.Add(e.Action));
        using (sub)
        {
            manager.Clear();
        }

        // Reset would force a full resync that races with the follow-up Add of
        // the new initial entry on a UI-thread mirror; the Clear implementation
        // must use granular events instead.
        Assert.That(actions, Does.Not.Contain(NotifyCollectionChangedAction.Reset));
        Assert.That(manager.Entries.Count, Is.EqualTo(1));
        Assert.That(manager.Entries[0].IsInitial, Is.True);
    }

    #endregion

    #region BeforeMutation Tests

    [TestCase(false)]
    [TestCase(true)]
    public void BeforeMutation_transaction_flush_does_not_recursively_publish(bool crossThread)
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        int calls = 0;
        bool flushed = false;
        using var subscription = manager.BeforeMutation.Subscribe(_ =>
        {
            calls++;
            if (!flushed)
            {
                flushed = true;
                void Flush() => manager.ExecuteInTransaction(() => manager.Record(CreateTestOperation()), "Flush");
                if (crossThread)
                    Task.Run(Flush).GetAwaiter().GetResult();
                else
                    Flush();
            }
        });
        manager.ExecuteInTransaction(() => manager.Record(CreateTestOperation()), "Edit");
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(manager.UndoCount, Is.EqualTo(2));
        manager.FlushPendingMutations();
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public async Task BeforeMutation_deferred_work_can_publish_after_the_original_dispatch_completes()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? deferred = null;
        int calls = 0;
        using var subscription = manager.BeforeMutation.Subscribe(_ =>
        {
            if (++calls == 1)
                deferred = Task.Run(async () =>
                {
                    await release.Task;
                    manager.FlushPendingMutations();
                });
        });
        manager.FlushPendingMutations();
        release.TrySetResult();
        await deferred!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void BeforeMutation_Undo_FiresBeforeStackMutates()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("Step");

        bool undoStackStillFull = false;
        bool fired = false;
        using var subscription = manager.BeforeMutation.Subscribe(_ =>
        {
            fired = true;
            // CanUndo must still be observable as true when subscribers see the event;
            // otherwise a flush handler that inspects history state would see the
            // post-undo world.
            undoStackStillFull = manager.CanUndo;
        });

        manager.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(fired, Is.True);
            Assert.That(undoStackStillFull, Is.True);
            Assert.That(manager.CanUndo, Is.False);
        });
    }

    [Test]
    public void BeforeMutation_Redo_FiresBeforeStackMutates()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("Step");
        manager.Undo();

        bool redoStackStillFull = false;
        bool fired = false;
        using var subscription = manager.BeforeMutation.Subscribe(_ =>
        {
            fired = true;
            redoStackStillFull = manager.CanRedo;
        });

        manager.Redo();

        Assert.Multiple(() =>
        {
            Assert.That(fired, Is.True);
            Assert.That(redoStackStillFull, Is.True);
            Assert.That(manager.CanRedo, Is.False);
        });
    }

    [Test]
    public void BeforeMutation_SubscriberCommit_LandsAsSeparateUndoEntry()
    {
        // Reproduces the timeline-nudge flush contract: a subscriber Commit
        // during Undo must produce an undo entry of its own that gets popped
        // by the very Undo that triggered it, not merged with the prior entry.
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("First");

        using var subscription = manager.BeforeMutation.Subscribe(_ =>
        {
            // Simulate a debounced edit that was still pending in a flush handler.
            manager.Record(CreateTestOperation());
            manager.Commit("Pending");
        });

        int undoCountBefore = manager.UndoCount;
        manager.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(undoCountBefore, Is.EqualTo(1));
            // After commit-from-subscriber (push) + Undo (pop), the user's prior
            // commit must remain on the stack.
            Assert.That(manager.UndoCount, Is.EqualTo(1));
            Assert.That(manager.PeekUndo()!.DisplayName, Is.EqualTo("First"));
        });
    }

    [Test]
    public void BeforeMutation_SubscriberThrows_DoesNotAbortUndo()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("Step");

        using var subscription = manager.BeforeMutation.Subscribe(_ =>
            throw new InvalidOperationException("subscriber blew up"));

        Assert.DoesNotThrow(() => manager.Undo());
        Assert.That(manager.CanUndo, Is.False);
    }

    [Test]
    public void BeforeMutation_JumpTo_FiresBeforeWalkBegins()
    {
        // Pending nudge operations live on _currentTransaction; without a flush
        // before JumpTo walks the stacks, those operations would silently revert
        // and never reach the undo history.
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("First");

        using var subscription = manager.BeforeMutation.Subscribe(_ =>
        {
            manager.Record(CreateTestOperation());
            manager.Commit("Pending");
        });

        int undoCountBefore = manager.UndoCount;
        manager.JumpTo(0);

        Assert.Multiple(() =>
        {
            Assert.That(undoCountBefore, Is.EqualTo(1));
            // After flush (push) + JumpTo to 0 (walks both back to redo), no
            // entries should be on the undo stack but both should be recoverable.
            Assert.That(manager.UndoCount, Is.EqualTo(0));
            Assert.That(manager.RedoCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void BeforeMutation_Dispose_CompletesObservable()
    {
        var manager = new HistoryManager(_root, _sequenceGenerator);

        bool completed = false;
        using var subscription = manager.BeforeMutation.Subscribe(
            _ => { },
            () => completed = true);

        manager.Dispose();

        Assert.That(completed, Is.True);
    }

    [Test]
    public void BeforeMutation_ContinuesAfterAnObserverThrows()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        using IDisposable throwingSubscription = manager.BeforeMutation.Subscribe(_ =>
            throw new InvalidOperationException("observer failed"));
        int laterNotifications = 0;
        using IDisposable laterSubscription = manager.BeforeMutation.Subscribe(_ =>
            laterNotifications++);

        Assert.DoesNotThrow(() => manager.FlushPendingMutations());

        Assert.That(laterNotifications, Is.EqualTo(1));
    }

    [TestCase(0, ExpectedResult = true)]
    [TestCase(1, ExpectedResult = true)]
    [TestCase(2, ExpectedResult = false)]
    [TestCase(3, ExpectedResult = false)]
    [TestCase(-1, ExpectedResult = false)]
    public bool WouldJumpToMove_ReflectsWhetherJumpWouldMove(int index)
    {
        // Two commits: entries [initial, First, Second] (Count 3), CurrentIndex 2.
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("First");
        manager.Record(CreateTestOperation());
        manager.Commit("Second");

        return manager.WouldJumpToMove(index);
    }

    [Test]
    public void WouldJumpToMove_ShouldReturnTrue_ForCurrentIndexWithPendingTransaction()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("First");
        // CurrentIndex == 1. An uncommitted operation makes JumpTo(1) still mutate
        // (it rolls the pending transaction back) even though the index matches.
        manager.Record(CreateTestOperation());

        Assert.Multiple(() =>
        {
            Assert.That(manager.WouldJumpToMove(manager.CurrentIndex), Is.True);
            Assert.That(manager.WouldJumpToMove(99), Is.False);
        });
    }

    [Test]
    public void HasPendingOperations_ShouldTrackUncommittedRecord()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        Assert.That(manager.HasPendingOperations, Is.False);

        manager.Record(CreateTestOperation());
        Assert.That(manager.HasPendingOperations, Is.True);

        manager.Commit("Commit");
        Assert.That(manager.HasPendingOperations, Is.False);
    }

    [Test]
    public void FlushPendingMutations_ShouldFireBeforeMutation()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        int fired = 0;
        using var subscription = manager.BeforeMutation.Subscribe(_ => fired++);

        manager.FlushPendingMutations();

        Assert.That(fired, Is.EqualTo(1));
    }

    private sealed class EqualHistoryObserver : IObserver<HistoryState>
    {
        public int NotificationCount { get; private set; }

        public int CompletionCount { get; private set; }

        public void OnCompleted() => CompletionCount++;

        public void OnError(Exception error)
        {
        }

        public void OnNext(HistoryState value) => NotificationCount++;

        public override bool Equals(object? obj) => obj is EqualHistoryObserver;

        public override int GetHashCode() => 0;
    }

    #endregion
}
