using System.Collections.Specialized;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Editor;

public class HistoryManagerTests
{
    private TestCoreObject _root = null!;
    private OperationSequenceGenerator _sequenceGenerator = null!;

    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        _root = new TestCoreObject();
        _sequenceGenerator = new OperationSequenceGenerator();
    }

    [TearDown]
    public void TearDown()
    {
    }

    [Test]
    public void Constructor_ShouldInitializeWithCorrectDefaults()
    {
        // Arrange & Act
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        // Assert
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
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new HistoryManager(null!, _sequenceGenerator));
    }

    [Test]
    public void Constructor_ShouldThrowArgumentNullException_WhenSequenceGeneratorIsNull()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new HistoryManager(_root, null!));
    }

    [Test]
    public void Record_ShouldAddOperationToCurrentTransaction()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        var operation = CreateTestOperation();

        // Act
        manager.Record(operation);
        manager.Commit("Test");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(manager.CanUndo, Is.True);
            Assert.That(manager.UndoCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Record_ShouldThrowArgumentNullException_WhenOperationIsNull()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => manager.Record((ChangeOperation)null!));
    }

    [Test]
    public void Commit_ShouldPushCurrentTransactionToUndoStack()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        var operation = CreateTestOperation();

        // Act
        manager.Record(operation);
        manager.Commit("Test Operation");

        // Assert
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
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        // Act
        manager.Commit("Empty");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(manager.CanUndo, Is.False);
            Assert.That(manager.UndoCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void Commit_ShouldClearRedoStack()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("First");
        manager.Undo();
        Assert.That(manager.CanRedo, Is.True);

        // Act
        manager.Record(CreateTestOperation());
        manager.Commit("Second");

        // Assert
        Assert.That(manager.CanRedo, Is.False);
    }

    [Test]
    public void Rollback_ShouldRevertCurrentTransaction()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;

        var operation = CustomOperation.Create(
            () => _root.Value = 100,
            () => _root.Value = 0,
            _sequenceGenerator,
            "Set Value");

        manager.Record(operation);
        operation.Apply(new OperationExecutionContext(_root));
        Assert.That(_root.Value, Is.EqualTo(100));

        // Act
        manager.Rollback();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(_root.Value, Is.EqualTo(0));
            Assert.That(manager.CanUndo, Is.False);
        });
    }

    [Test]
    public void Undo_ShouldRevertLastCommittedTransaction()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;

        var operation = CustomOperation.Create(
            () => _root.Value = 100,
            () => _root.Value = 0,
            _sequenceGenerator,
            "Set Value");

        operation.Apply(new OperationExecutionContext(_root));
        manager.Record(operation);
        manager.Commit("Set Value");

        // Act
        var result = manager.Undo();

        // Assert
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
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        // Act
        var result = manager.Undo();

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void Redo_ShouldReapplyLastUndoneTransaction()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;

        var operation = CustomOperation.Create(
            () => _root.Value = 100,
            () => _root.Value = 0,
            _sequenceGenerator,
            "Set Value");

        operation.Apply(new OperationExecutionContext(_root));
        manager.Record(operation);
        manager.Commit("Set Value");
        manager.Undo();

        // Act
        var result = manager.Redo();

        // Assert
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
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        // Act
        var result = manager.Redo();

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void Clear_ShouldRemoveAllHistory()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("First");
        manager.Record(CreateTestOperation());
        manager.Commit("Second");
        manager.Undo();

        // Act
        manager.Clear();

        // Assert
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
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("First");
        manager.Record(CreateTestOperation());
        manager.Commit("Second");

        // Act
        var transaction = manager.PeekUndo();

        // Assert
        Assert.That(transaction?.DisplayName, Is.EqualTo("Second"));
    }

    [Test]
    public void PeekUndo_ShouldReturnNull_WhenNoUndoAvailable()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        // Act
        var transaction = manager.PeekUndo();

        // Assert
        Assert.That(transaction, Is.Null);
    }

    [Test]
    public void PeekRedo_ShouldReturnLastUndoneTransaction()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("First");
        manager.Undo();

        // Act
        var transaction = manager.PeekRedo();

        // Assert
        Assert.That(transaction?.DisplayName, Is.EqualTo("First"));
    }

    [Test]
    public void PeekRedo_ShouldReturnNull_WhenNoRedoAvailable()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        // Act
        var transaction = manager.PeekRedo();

        // Assert
        Assert.That(transaction, Is.Null);
    }

    [Test]
    public void ExecuteInTransaction_ShouldCommitOnSuccess()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;

        // Act
        manager.ExecuteInTransaction(() =>
        {
            var operation = CustomOperation.Create(
                () => _root.Value = 100,
                () => _root.Value = 0,
                _sequenceGenerator,
                "Set Value");
            operation.Apply(new OperationExecutionContext(_root));
            manager.Record(operation);
        }, "Transaction");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(manager.CanUndo, Is.True);
            Assert.That(manager.PeekUndo()?.DisplayName, Is.EqualTo("Transaction"));
        });
    }

    [Test]
    public void ExecuteInTransaction_ShouldRollbackOnException()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            manager.ExecuteInTransaction(() =>
            {
                var operation = CustomOperation.Create(
                    () => _root.Value = 100,
                    () => _root.Value = 0,
                    _sequenceGenerator,
                    "Set Value");
                operation.Apply(new OperationExecutionContext(_root));
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
    public void Record_WithActions_ShouldCreateCustomOperation()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;
        bool doExecuted = false;

        // Act
        manager.Record(
            () => { doExecuted = true; _root.Value = 100; },
            () => { _root.Value = 0; },
            "Custom Action");
        manager.Commit("Test");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(doExecuted, Is.False);
            Assert.That(manager.CanUndo, Is.True);
        });
    }

    [Test]
    public void StateChanged_ShouldBeNotified_OnCommit()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        HistoryState? receivedState = null;
        using var subscription = manager.StateChanged.Subscribe(state => receivedState = state);

        // Act
        manager.Record(CreateTestOperation());
        manager.Commit("Test");

        // Assert
        Assert.That(receivedState, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(receivedState!.Value.CanUndo, Is.True);
            Assert.That(receivedState!.Value.CanRedo, Is.False);
            Assert.That(receivedState!.Value.UndoCount, Is.EqualTo(1));
            Assert.That(receivedState!.Value.RedoCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void StateChanged_ShouldBeNotified_OnUndo()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("Test");

        HistoryState? receivedState = null;
        using var subscription = manager.StateChanged.Subscribe(state => receivedState = state);

        // Act
        manager.Undo();

        // Assert
        Assert.That(receivedState, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(receivedState!.Value.CanUndo, Is.False);
            Assert.That(receivedState!.Value.CanRedo, Is.True);
        });
    }

    [Test]
    public void StateChanged_ShouldBeNotified_OnRedo()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("Test");
        manager.Undo();

        HistoryState? receivedState = null;
        using var subscription = manager.StateChanged.Subscribe(state => receivedState = state);

        // Act
        manager.Redo();

        // Assert
        Assert.That(receivedState, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(receivedState!.Value.CanUndo, Is.True);
            Assert.That(receivedState!.Value.CanRedo, Is.False);
        });
    }

    [Test]
    public void StateChanged_ShouldBeNotified_OnClear()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("Test");

        HistoryState? receivedState = null;
        using var subscription = manager.StateChanged.Subscribe(state => receivedState = state);

        // Act
        manager.Clear();

        // Assert
        Assert.That(receivedState, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(receivedState!.Value.CanUndo, Is.False);
            Assert.That(receivedState!.Value.CanRedo, Is.False);
        });
    }

    [Test]
    public void Dispose_ShouldPreventFurtherOperations()
    {
        // Arrange
        var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => manager.Record(CreateTestOperation()));
        Assert.Throws<ObjectDisposedException>(() => manager.Commit("Test"));
        Assert.Throws<ObjectDisposedException>(() => manager.Undo());
        Assert.Throws<ObjectDisposedException>(() => manager.Redo());
        Assert.Throws<ObjectDisposedException>(() => manager.Clear());
    }

    [Test]
    public void MultipleUndoRedo_ShouldMaintainCorrectState()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;

        for (int i = 1; i <= 3; i++)
        {
            int value = i * 100;
            int prevValue = (i - 1) * 100;
            var operation = CustomOperation.Create(
                () => _root.Value = value,
                () => _root.Value = prevValue,
                _sequenceGenerator,
                $"Set to {value}");
            operation.Apply(new OperationExecutionContext(_root));
            manager.Record(operation);
            manager.Commit($"Step {i}");
        }

        // Act & Assert
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
            _sequenceGenerator,
            "Test Operation");
    }

    private CustomOperation CreateValueOperation(HistoryManager manager, int targetValue, int previousValue, string description)
    {
        var op = CustomOperation.Create(
            () => _root.Value = targetValue,
            () => _root.Value = previousValue,
            _sequenceGenerator,
            description);
        op.Apply(new OperationExecutionContext(_root));
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
        CreateValueOperation(manager, 100, 0, "Set");
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
        CreateValueOperation(manager, 100, 0, "Step1");
        manager.Commit("Step1");
        CreateValueOperation(manager, 200, 100, "Step2");
        manager.Commit("Step2");

        manager.Undo();
        Assert.That(manager.Entries.Count, Is.EqualTo(3));

        CreateValueOperation(manager, 50, 100, "Replacement");
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
        CreateValueOperation(manager, 100, 0, "Step1");
        manager.Commit("Step1");
        CreateValueOperation(manager, 200, 100, "Step2");
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
        CreateValueOperation(manager, 100, 0, "Step1");
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
        CreateValueOperation(manager, 100, 0, "Step1");
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
        CreateValueOperation(manager, 100, 0, "Step1");
        manager.Commit("Step1");
        CreateValueOperation(manager, 200, 100, "Step2");
        manager.Commit("Step2");
        CreateValueOperation(manager, 300, 200, "Step3");
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
        CreateValueOperation(manager, 100, 0, "Step1");
        manager.Commit("Step1");
        CreateValueOperation(manager, 200, 100, "Step2");
        manager.Commit("Step2");
        CreateValueOperation(manager, 300, 200, "Step3");
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
        CreateValueOperation(manager, 100, 0, "Committed");
        manager.Commit("Committed");

        // Uncommitted operation that mutates state.
        var pending = CustomOperation.Create(
            () => _root.Value = 999,
            () => _root.Value = 100,
            _sequenceGenerator,
            "Pending");
        pending.Apply(new OperationExecutionContext(_root));
        manager.Record(pending);

        bool moved = manager.JumpTo(0);

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True);
            Assert.That(_root.Value, Is.EqualTo(0));
            // The uncommitted operation must not survive into a future commit.
            CreateValueOperation(manager, 50, 0, "AfterJump");
            manager.Commit("AfterJump");
            Assert.That(manager.PeekUndo()?.OperationCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void JumpTo_ToInitialIndex_ShouldRestoreInitialState()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;
        CreateValueOperation(manager, 100, 0, "Step1");
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
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
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

    [Test]
    public void HistoryEntry_Subtypes_AreSealedDiscriminatedUnion()
    {
        var initial = HistoryEntry.CreateInitial();
        var transactional = HistoryEntry.FromTransaction(new HistoryTransaction(7));

        Assert.Multiple(() =>
        {
            Assert.That(initial, Is.InstanceOf<InitialHistoryEntry>());
            Assert.That(transactional, Is.InstanceOf<TransactionHistoryEntry>());
            Assert.That(initial.TransactionId, Is.Null);
            Assert.That(transactional.TransactionId, Is.EqualTo(7L));
        });
    }

    #endregion

    #region JumpTo Failure / Disposal Tests

    [Test]
    public void JumpTo_AfterDispose_ShouldThrowObjectDisposedException()
    {
        var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Dispose();

        Assert.Throws<ObjectDisposedException>(() => manager.JumpTo(0));
    }

    [Test]
    public void JumpTo_WhenApplyThrowsMidForwardLoop_ShouldKeepFailingTransactionOnRedoStack()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;

        CreateValueOperation(manager, 100, 0, "Step1");
        manager.Commit("Step1");
        CreateValueOperation(manager, 200, 100, "Step2");
        manager.Commit("Step2");

        // Faulty's first Apply (the explicit pre-record call) succeeds; any
        // subsequent Apply (e.g. via Redo / JumpTo forward) throws.
        bool firstApply = true;
        var faulty = CustomOperation.Create(
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
            () => _root.Value = 200,
            _sequenceGenerator,
            "Faulty");
        faulty.Apply(new OperationExecutionContext(_root));
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
        var faulty = CustomOperation.Create(
            () => _root.Value = 100,
            () => throw new InvalidOperationException("Boom"),
            _sequenceGenerator,
            "Faulty");
        faulty.Apply(new OperationExecutionContext(_root));
        manager.Record(faulty);
        manager.Commit("Faulty");

        CreateValueOperation(manager, 200, 100, "Step2");
        manager.Commit("Step2");
        CreateValueOperation(manager, 300, 200, "Step3");
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
        CreateValueOperation(manager, 100, 0, "Committed");
        manager.Commit("Committed");

        // Pending op whose revert throws — but its operation should NOT survive
        // into the next commit because JumpTo's finally swaps _currentTransaction.
        var pending = CustomOperation.Create(
            () => _root.Value = 999,
            () => throw new InvalidOperationException("PendingRevertFailed"),
            _sequenceGenerator,
            "Pending");
        pending.Apply(new OperationExecutionContext(_root));
        manager.Record(pending);

        Assert.Throws<InvalidOperationException>(() => manager.JumpTo(0));

        // A subsequent commit must not include the pending operation.
        CreateValueOperation(manager, 50, 999, "After");
        manager.Commit("After");

        Assert.That(manager.PeekUndo()?.OperationCount, Is.EqualTo(1));
    }

    [Test]
    public void JumpTo_RejumpForwardAfterCommitCollapse_ShouldFail()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;
        CreateValueOperation(manager, 100, 0, "Step1");
        manager.Commit("Step1");
        CreateValueOperation(manager, 200, 100, "Step2");
        manager.Commit("Step2");
        CreateValueOperation(manager, 300, 200, "Step3");
        manager.Commit("Step3");

        // Jump back, then commit -> redo stack collapses, entries truncate.
        manager.JumpTo(1);
        CreateValueOperation(manager, 50, 100, "Replacement");
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
        CreateValueOperation(manager, 100, 0, "Step1");
        manager.Commit("Step1");

        HistoryEntry[] snapshot = manager.GetEntriesSnapshot();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Length, Is.EqualTo(2));
            Assert.That(snapshot[0].IsInitial, Is.True);
            Assert.That(snapshot[1].DisplayName, Is.EqualTo("Step1"));
        });

        // Mutating the manager must not affect the snapshot reference.
        CreateValueOperation(manager, 200, 100, "Step2");
        manager.Commit("Step2");
        Assert.That(snapshot.Length, Is.EqualTo(2));
        Assert.That(manager.Entries.Count, Is.EqualTo(3));
    }

    [Test]
    public void GetEntriesSnapshot_AfterDispose_ShouldThrow()
    {
        var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Dispose();

        Assert.Throws<ObjectDisposedException>(() => manager.GetEntriesSnapshot());
    }

    [Test]
    public void SubscribeEntries_ReturnsCurrentSnapshotAndForwardsLaterChanges()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        CreateValueOperation(manager, 100, 0, "Step1");
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

            CreateValueOperation(manager, 200, 100, "Step2");
            manager.Commit("Step2");

            Assert.That(received, Does.Contain(NotifyCollectionChangedAction.Add));
        }
    }

    [Test]
    public void SubscribeEntries_DisposalUnsubscribesHandler()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        int callCount = 0;
        var (sub, _, _) = manager.SubscribeEntries((_, _) => callCount++);

        sub.Dispose();

        CreateValueOperation(manager, 1, 0, "AfterUnsubscribe");
        manager.Commit("AfterUnsubscribe");

        Assert.That(callCount, Is.EqualTo(0));
    }

    [Test]
    public void SubscribeEntries_AfterDispose_ShouldThrow()
    {
        var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            manager.SubscribeEntries((_, _) => { }));
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
        CreateValueOperation(manager, 100, 0, "Step1");
        manager.Commit("Step1");
        CreateValueOperation(manager, 200, 100, "Step2");
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

    #region BeginRecordingScope Tests

    [Test]
    public void BeginRecordingScope_ShouldReturnValidScope()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        // Act
        using var scope = manager.BeginRecordingScope(
            () => _root.Value,
            value => _root.Value = value,
            "Test Scope");

        // Assert
        Assert.That(scope, Is.Not.Null);
    }

    [Test]
    public void BeginRecordingScope_ShouldThrowArgumentNullException_WhenCaptureStateIsNull()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            manager.BeginRecordingScope<int>(
                null!,
                value => _root.Value = value,
                "Test"));
    }

    [Test]
    public void BeginRecordingScope_ShouldThrowArgumentNullException_WhenApplyStateIsNull()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            manager.BeginRecordingScope(
                () => _root.Value,
                null!,
                "Test"));
    }

    [Test]
    public void BeginRecordingScope_ShouldThrowObjectDisposedException_WhenManagerIsDisposed()
    {
        // Arrange
        var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() =>
            manager.BeginRecordingScope(
                () => _root.Value,
                value => _root.Value = value,
                "Test"));
    }

    [Test]
    public void RecordingScope_Complete_ShouldRecordOperation()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 10;

        // Act
        using (var scope = manager.BeginRecordingScope(
            () => _root.Value,
            value => _root.Value = value,
            "Set Value"))
        {
            _root.Value = 50;
            scope.Complete();
        }
        manager.Commit("Test");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(manager.CanUndo, Is.True);
            Assert.That(manager.UndoCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void RecordingScope_Complete_ShouldCaptureBeforeAndAfterState()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 10;

        // Act
        using (var scope = manager.BeginRecordingScope(
            () => _root.Value,
            value => _root.Value = value,
            "Set Value"))
        {
            _root.Value = 50;
            scope.Complete();
        }
        manager.Commit("Test");

        // Assert - Value should still be 50 after complete
        Assert.That(_root.Value, Is.EqualTo(50));

        // Undo should restore to 10
        manager.Undo();
        Assert.That(_root.Value, Is.EqualTo(10));

        // Redo should restore to 50
        manager.Redo();
        Assert.That(_root.Value, Is.EqualTo(50));
    }

    [Test]
    public void RecordingScope_Cancel_ShouldNotRecordOperation()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 10;

        // Act
        using (var scope = manager.BeginRecordingScope(
            () => _root.Value,
            value => _root.Value = value,
            "Set Value"))
        {
            _root.Value = 50;
            scope.Cancel();
        }
        manager.Commit("Test");

        // Assert - No operation recorded since we cancelled
        Assert.That(manager.CanUndo, Is.False);
        Assert.That(_root.Value, Is.EqualTo(50)); // Value was still changed
    }

    [Test]
    public void RecordingScope_Dispose_ShouldAutoCompleteIfNotCancelled()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 10;

        // Act - Using statement will auto-dispose and auto-complete
        using (var scope = manager.BeginRecordingScope(
            () => _root.Value,
            value => _root.Value = value,
            "Set Value"))
        {
            _root.Value = 50;
            // No explicit Complete() or Cancel()
        }
        manager.Commit("Test");

        // Assert - Should have auto-completed
        Assert.Multiple(() =>
        {
            Assert.That(manager.CanUndo, Is.True);
            Assert.That(_root.Value, Is.EqualTo(50));
        });

        // Verify undo works
        manager.Undo();
        Assert.That(_root.Value, Is.EqualTo(10));
    }

    [Test]
    public void RecordingScope_Dispose_ShouldNotRecordAfterCancel()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 10;

        // Act
        using (var scope = manager.BeginRecordingScope(
            () => _root.Value,
            value => _root.Value = value,
            "Set Value"))
        {
            _root.Value = 50;
            scope.Cancel();
            // Dispose will be called but should not record
        }
        manager.Commit("Test");

        // Assert
        Assert.That(manager.CanUndo, Is.False);
    }

    [Test]
    public void RecordingScope_DoubleComplete_ShouldOnlyRecordOnce()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 10;

        // Act
        using (var scope = manager.BeginRecordingScope(
            () => _root.Value,
            value => _root.Value = value,
            "Set Value"))
        {
            _root.Value = 50;
            scope.Complete();
            scope.Complete(); // Second call should be ignored
        }
        manager.Commit("Test");

        // Assert - Should only have one operation
        Assert.That(manager.UndoCount, Is.EqualTo(1));
    }

    [Test]
    public void RecordingScope_CompleteAfterCancel_ShouldNotRecord()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 10;

        // Act
        using (var scope = manager.BeginRecordingScope(
            () => _root.Value,
            value => _root.Value = value,
            "Set Value"))
        {
            _root.Value = 50;
            scope.Cancel();
            scope.Complete(); // Should be ignored after cancel
        }
        manager.Commit("Test");

        // Assert
        Assert.That(manager.CanUndo, Is.False);
    }

    [Test]
    public void RecordingScope_WithComplexState_ShouldCaptureCorrectly()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        var state = new TestState { Value1 = 1, Value2 = "A" };

        // Act
        using (var scope = manager.BeginRecordingScope(
            () => new TestState { Value1 = state.Value1, Value2 = state.Value2 },
            s => { state.Value1 = s.Value1; state.Value2 = s.Value2; },
            "Change State"))
        {
            state.Value1 = 100;
            state.Value2 = "Z";
            scope.Complete();
        }
        manager.Commit("Test");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(state.Value1, Is.EqualTo(100));
            Assert.That(state.Value2, Is.EqualTo("Z"));
        });

        // Undo
        manager.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(state.Value1, Is.EqualTo(1));
            Assert.That(state.Value2, Is.EqualTo("A"));
        });

        // Redo
        manager.Redo();
        Assert.Multiple(() =>
        {
            Assert.That(state.Value1, Is.EqualTo(100));
            Assert.That(state.Value2, Is.EqualTo("Z"));
        });
    }

    [Test]
    public void RecordingScope_WithNullDescription_ShouldWork()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 10;

        // Act
        using (var scope = manager.BeginRecordingScope(
            () => _root.Value,
            value => _root.Value = value))
        {
            _root.Value = 50;
            scope.Complete();
        }
        manager.Commit("Test");

        // Assert
        Assert.That(manager.CanUndo, Is.True);
    }

    [Test]
    public void RecordingScope_NoStateChange_ShouldStillRecord()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 10;

        // Act - No actual state change
        using (var scope = manager.BeginRecordingScope(
            () => _root.Value,
            value => _root.Value = value,
            "No Change"))
        {
            // Don't change anything
            scope.Complete();
        }
        manager.Commit("Test");

        // Assert - Operation is still recorded (even if state didn't change)
        Assert.That(manager.CanUndo, Is.True);
    }

    [Test]
    public void RecordingScope_MultipleScopes_ShouldWorkIndependently()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;
        var state2 = new { Value = 0 };
        int value2 = 0;

        // Act - First scope
        using (var scope1 = manager.BeginRecordingScope(
            () => _root.Value,
            v => _root.Value = v,
            "First"))
        {
            _root.Value = 10;
            scope1.Complete();
        }

        // Second scope
        using (var scope2 = manager.BeginRecordingScope(
            () => value2,
            v => value2 = v,
            "Second"))
        {
            value2 = 20;
            scope2.Complete();
        }

        manager.Commit("Test");

        // Assert
        Assert.That(manager.UndoCount, Is.EqualTo(1)); // Both in same transaction

        // Undo should revert both
        manager.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(_root.Value, Is.EqualTo(0));
            Assert.That(value2, Is.EqualTo(0));
        });
    }

    [Test]
    public void RecordingScope_IntegrationWithOtherOperations_ShouldWork()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;

        // Act - Mix scope with regular operations
        using (var scope = manager.BeginRecordingScope(
            () => _root.Value,
            v => _root.Value = v,
            "Scope Op"))
        {
            _root.Value = 10;
            scope.Complete();
        }

        // Add another operation directly
        var operation = CustomOperation.Create(
            () => _root.Value = 100,
            () => _root.Value = 10,
            _sequenceGenerator,
            "Direct Op");
        operation.Apply(new OperationExecutionContext(_root));
        manager.Record(operation);

        manager.Commit("Combined");

        // Assert
        Assert.That(_root.Value, Is.EqualTo(100));
        Assert.That(manager.UndoCount, Is.EqualTo(1));

        // Undo should revert all operations in the transaction
        manager.Undo();
        Assert.That(_root.Value, Is.EqualTo(0));
    }

    [Test]
    public void RecordingScope_WithReferenceTypeState_ShouldCaptureSnapshot()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        var data = new List<int> { 1, 2, 3 };

        // Act
        using (var scope = manager.BeginRecordingScope(
            () => data.ToList(), // Capture a snapshot
            state => { data.Clear(); data.AddRange(state); },
            "Modify List"))
        {
            data.Add(4);
            data.Add(5);
            scope.Complete();
        }
        manager.Commit("Test");

        // Assert
        Assert.That(data, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));

        // Undo
        manager.Undo();
        Assert.That(data, Is.EqualTo(new[] { 1, 2, 3 }));

        // Redo
        manager.Redo();
        Assert.That(data, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
    }

    [Test]
    public void RecordingScope_CaptureStateThrows_ShouldPropagateException()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);

        // Act & Assert - Exception during initial state capture
        Assert.Throws<InvalidOperationException>(() =>
            manager.BeginRecordingScope<int>(
                () => throw new InvalidOperationException("Capture failed"),
                value => _root.Value = value,
                "Test"));
    }

    [Test]
    public void RecordingScope_ApplyStateInUndo_ShouldWork()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        int applyCount = 0;
        _root.Value = 10;

        // Act
        using (var scope = manager.BeginRecordingScope(
            () => _root.Value,
            value =>
            {
                applyCount++;
                _root.Value = value;
            },
            "Test"))
        {
            _root.Value = 50;
            scope.Complete();
        }
        manager.Commit("Test");

        // Reset counter
        applyCount = 0;

        // Undo should call applyState with beforeState
        manager.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(applyCount, Is.EqualTo(1));
            Assert.That(_root.Value, Is.EqualTo(10));
        });

        // Redo should call applyState with afterState
        manager.Redo();
        Assert.Multiple(() =>
        {
            Assert.That(applyCount, Is.EqualTo(2));
            Assert.That(_root.Value, Is.EqualTo(50));
        });
    }

    [Test]
    public void RecordingScope_WithNestedScopes_ShouldWorkCorrectly()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;
        int value2 = 0;

        // Act - Nested scopes
        using (var outer = manager.BeginRecordingScope(
            () => _root.Value,
            v => _root.Value = v,
            "Outer"))
        {
            _root.Value = 10;

            using (var inner = manager.BeginRecordingScope(
                () => value2,
                v => value2 = v,
                "Inner"))
            {
                value2 = 20;
                inner.Complete();
            }

            outer.Complete();
        }

        manager.Commit("Test");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(_root.Value, Is.EqualTo(10));
            Assert.That(value2, Is.EqualTo(20));
        });

        // Undo both
        manager.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(_root.Value, Is.EqualTo(0));
            Assert.That(value2, Is.EqualTo(0));
        });
    }

    [Test]
    public void RecordingScope_WithNullableValueType_ShouldWork()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        int? nullableValue = null;

        // Act
        using (var scope = manager.BeginRecordingScope(
            () => nullableValue,
            v => nullableValue = v,
            "Set Nullable"))
        {
            nullableValue = 42;
            scope.Complete();
        }
        manager.Commit("Test");

        // Assert
        Assert.That(nullableValue, Is.EqualTo(42));

        manager.Undo();
        Assert.That(nullableValue, Is.Null);

        manager.Redo();
        Assert.That(nullableValue, Is.EqualTo(42));
    }

    [Test]
    public void RecordingScope_WithNullReferenceType_ShouldWork()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        string? stringValue = null;

        // Act
        using (var scope = manager.BeginRecordingScope(
            () => stringValue,
            v => stringValue = v,
            "Set String"))
        {
            stringValue = "Hello";
            scope.Complete();
        }
        manager.Commit("Test");

        // Assert
        Assert.That(stringValue, Is.EqualTo("Hello"));

        manager.Undo();
        Assert.That(stringValue, Is.Null);
    }

    [Test]
    public void RecordingScope_SeparateTransactions_ShouldBeIndependent()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;

        // Act - First transaction
        using (var scope = manager.BeginRecordingScope(
            () => _root.Value,
            v => _root.Value = v,
            "First"))
        {
            _root.Value = 10;
            scope.Complete();
        }
        manager.Commit("First Transaction");

        // Second transaction
        using (var scope = manager.BeginRecordingScope(
            () => _root.Value,
            v => _root.Value = v,
            "Second"))
        {
            _root.Value = 20;
            scope.Complete();
        }
        manager.Commit("Second Transaction");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(manager.UndoCount, Is.EqualTo(2));
            Assert.That(_root.Value, Is.EqualTo(20));
        });

        // Undo second
        manager.Undo();
        Assert.That(_root.Value, Is.EqualTo(10));

        // Undo first
        manager.Undo();
        Assert.That(_root.Value, Is.EqualTo(0));
    }

    [Test]
    public void RecordingScope_UndoRedoMultipleTimes_ShouldMaintainCorrectState()
    {
        // Arrange
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;

        using (var scope = manager.BeginRecordingScope(
            () => _root.Value,
            v => _root.Value = v,
            "Test"))
        {
            _root.Value = 100;
            scope.Complete();
        }
        manager.Commit("Test");

        // Act & Assert - Multiple undo/redo cycles
        for (int i = 0; i < 3; i++)
        {
            manager.Undo();
            Assert.That(_root.Value, Is.EqualTo(0), $"Undo cycle {i}");

            manager.Redo();
            Assert.That(_root.Value, Is.EqualTo(100), $"Redo cycle {i}");
        }
    }

    private class TestState
    {
        public int Value1 { get; set; }
        public string Value2 { get; set; } = string.Empty;
    }

    #endregion

    #region BeforeMutation Tests

    [Test]
    public void BeforeMutation_Undo_FiresWithUndoKindBeforeStackMutates()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("Step");

        bool undoStackStillFull = false;
        HistoryMutationKind? observedKind = null;
        using var subscription = manager.BeforeMutation.Subscribe(kind =>
        {
            observedKind = kind;
            // CanUndo must still be observable as true when subscribers see the event;
            // otherwise a flush handler that inspects history state would see the
            // post-undo world.
            undoStackStillFull = manager.CanUndo;
        });

        manager.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(observedKind, Is.EqualTo(HistoryMutationKind.Undo));
            Assert.That(undoStackStillFull, Is.True);
            Assert.That(manager.CanUndo, Is.False);
        });
    }

    [Test]
    public void BeforeMutation_Redo_FiresWithRedoKindBeforeStackMutates()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("Step");
        manager.Undo();

        bool redoStackStillFull = false;
        HistoryMutationKind? observedKind = null;
        using var subscription = manager.BeforeMutation.Subscribe(kind =>
        {
            observedKind = kind;
            redoStackStillFull = manager.CanRedo;
        });

        manager.Redo();

        Assert.Multiple(() =>
        {
            Assert.That(observedKind, Is.EqualTo(HistoryMutationKind.Redo));
            Assert.That(redoStackStillFull, Is.True);
            Assert.That(manager.CanRedo, Is.False);
        });
    }

    [Test]
    public void BeforeMutation_RedoSubscriberDiscardingPendingOps_KeepsRedoEntry()
    {
        // Regression for the redo-stack wipeout: when a subscriber commits inside
        // a Redo's BeforeMutation, Commit clears _redoStack and the very Redo that
        // triggered it can no longer pop anything. The contract is that a flush
        // handler must inspect the kind and only commit on Undo/JumpTo. We
        // verify by simulating the correct behavior: discard pending ops (rollback)
        // on Redo, and confirm the Redo still completes against the original entry.
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        _root.Value = 0;
        CreateValueOperation(manager, 100, 0, "TargetForRedo");
        manager.Commit("TargetForRedo");
        manager.Undo();

        using var subscription = manager.BeforeMutation.Subscribe(kind =>
        {
            if (kind == HistoryMutationKind.Redo)
            {
                // Correct subscriber: only rollback pending, do NOT commit.
                manager.Rollback();
            }
        });

        // Simulate a pending uncommitted op (e.g. nudge in debounce).
        manager.Record(CreateTestOperation());

        bool redone = manager.Redo();

        Assert.Multiple(() =>
        {
            Assert.That(redone, Is.True);
            Assert.That(_root.Value, Is.EqualTo(100));
            Assert.That(manager.CanRedo, Is.False);
            Assert.That(manager.UndoCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void BeforeMutation_JumpTo_FiresWithJumpToKind()
    {
        using var manager = new HistoryManager(_root, _sequenceGenerator);
        manager.Record(CreateTestOperation());
        manager.Commit("Step");

        HistoryMutationKind? observedKind = null;
        using var subscription = manager.BeforeMutation.Subscribe(kind => observedKind = kind);

        manager.JumpTo(0);

        Assert.That(observedKind, Is.EqualTo(HistoryMutationKind.JumpTo));
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

    #endregion
}
