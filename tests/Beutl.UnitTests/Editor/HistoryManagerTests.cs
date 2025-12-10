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

    private class TestCoreObject : CoreObject
    {
        public int Value { get; set; }
    }
}
