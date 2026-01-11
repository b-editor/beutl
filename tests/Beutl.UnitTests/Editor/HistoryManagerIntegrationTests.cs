using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using static Beutl.Collections.ResetBehavior;

namespace Beutl.UnitTests.Editor;

/// <summary>
/// Integration tests for HistoryManager with CoreObjectOperationObserver.
/// These tests verify that property changes are automatically recorded and can be undone/redone.
/// </summary>
public class HistoryManagerIntegrationTests
{
    private TestModel _root = null!;
    private OperationSequenceGenerator _sequenceGenerator = null!;
    private HistoryManager _historyManager = null!;
    private CoreObjectOperationObserver _observer = null!;

    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        _root = new TestModel();
        _sequenceGenerator = new OperationSequenceGenerator();
        _historyManager = new HistoryManager(_root, _sequenceGenerator);
        _observer = new CoreObjectOperationObserver(null, _root, _sequenceGenerator);
        _historyManager.Subscribe(_observer);
    }

    [TearDown]
    public void TearDown()
    {
        _observer.Dispose();
        _historyManager.Dispose();
    }

    #region Simple Property Change Tests

    [Test]
    public void PropertyChange_ShouldBeRecorded_AndUndoable()
    {
        // Arrange
        _root.IntValue = 0;

        // Act
        _root.IntValue = 100;
        _historyManager.Commit("Change IntValue");

        // Assert
        Assert.That(_historyManager.CanUndo, Is.True);
        Assert.That(_root.IntValue, Is.EqualTo(100));

        // Undo
        _historyManager.Undo();
        Assert.That(_root.IntValue, Is.EqualTo(0));
    }

    [Test]
    public void PropertyChange_ShouldBeRedoable_AfterUndo()
    {
        // Arrange
        _root.IntValue = 0;
        _root.IntValue = 100;
        _historyManager.Commit("Change IntValue");
        _historyManager.Undo();

        // Act
        _historyManager.Redo();

        // Assert
        Assert.That(_root.IntValue, Is.EqualTo(100));
    }

    [Test]
    public void StringPropertyChange_ShouldBeRecorded_AndUndoable()
    {
        // Arrange - Set initial value and commit to establish baseline
        _root.StringValue = "Initial";
        _historyManager.Commit("Set initial");

        // Act
        _root.StringValue = "Modified";
        _historyManager.Commit("Change StringValue");

        // Assert
        Assert.That(_historyManager.CanUndo, Is.True);
        Assert.That(_root.StringValue, Is.EqualTo("Modified"));

        // Undo
        _historyManager.Undo();
        Assert.That(_root.StringValue, Is.EqualTo("Initial"));
    }

    [Test]
    public void NullablePropertyChange_ShouldBeRecorded_AndUndoable()
    {
        // Arrange
        _root.NullableValue = null;

        // Act
        _root.NullableValue = 42;
        _historyManager.Commit("Set NullableValue");

        // Assert
        Assert.That(_root.NullableValue, Is.EqualTo(42));

        // Undo
        _historyManager.Undo();
        Assert.That(_root.NullableValue, Is.Null);
    }

    #endregion

    #region Multiple Property Changes in Single Transaction

    [Test]
    public void MultiplePropertyChanges_InSingleTransaction_ShouldBeUndoneAtOnce()
    {
        // Arrange - Set initial values and commit
        _root.IntValue = 0;
        _root.StringValue = "Initial";
        _root.DoubleValue = 0.0;
        _historyManager.Commit("Set initial values");

        // Act
        _root.IntValue = 100;
        _root.StringValue = "Modified";
        _root.DoubleValue = 3.14;
        _historyManager.Commit("Multiple changes");

        // Assert
        Assert.That(_historyManager.UndoCount, Is.EqualTo(2));

        // Undo
        _historyManager.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(_root.IntValue, Is.EqualTo(0));
            Assert.That(_root.StringValue, Is.EqualTo("Initial"));
            Assert.That(_root.DoubleValue, Is.EqualTo(0.0));
        });
    }

    [Test]
    public void MultipleTransactions_ShouldBeUndoneIndividually()
    {
        // Arrange
        _root.IntValue = 0;

        // Act
        _root.IntValue = 10;
        _historyManager.Commit("First");

        _root.IntValue = 20;
        _historyManager.Commit("Second");

        _root.IntValue = 30;
        _historyManager.Commit("Third");

        // Assert
        Assert.That(_historyManager.UndoCount, Is.EqualTo(3));
        Assert.That(_root.IntValue, Is.EqualTo(30));

        // Undo one by one
        _historyManager.Undo();
        Assert.That(_root.IntValue, Is.EqualTo(20));

        _historyManager.Undo();
        Assert.That(_root.IntValue, Is.EqualTo(10));

        _historyManager.Undo();
        Assert.That(_root.IntValue, Is.EqualTo(0));
    }

    #endregion

    #region Nested Object Property Changes

    [Test]
    public void NestedObjectPropertyChange_ShouldBeRecorded()
    {
        // Arrange
        var nested = new TestNestedModel { NestedIntValue = 0 };
        _root.NestedObject = nested;
        _historyManager.Commit("Set nested object");

        // Act
        nested.NestedIntValue = 999;
        _historyManager.Commit("Change nested property");

        // Assert
        Assert.That(_historyManager.UndoCount, Is.EqualTo(2));
        Assert.That(nested.NestedIntValue, Is.EqualTo(999));

        // Undo nested property change
        _historyManager.Undo();
        Assert.That(nested.NestedIntValue, Is.EqualTo(0));
    }

    [Test]
    public void NestedObjectReplacement_ShouldBeRecorded()
    {
        // Arrange
        var nested1 = new TestNestedModel { NestedIntValue = 100 };
        var nested2 = new TestNestedModel { NestedIntValue = 200 };
        _root.NestedObject = nested1;
        _historyManager.Commit("Set first nested");

        // Act
        _root.NestedObject = nested2;
        _historyManager.Commit("Replace nested");

        // Assert
        Assert.That(_root.NestedObject, Is.EqualTo(nested2));
        Assert.That(_root.NestedObject!.NestedIntValue, Is.EqualTo(200));

        // Undo
        _historyManager.Undo();
        Assert.That(_root.NestedObject, Is.EqualTo(nested1));
        Assert.That(_root.NestedObject!.NestedIntValue, Is.EqualTo(100));
    }

    #endregion

    #region Rollback Tests

    [Test]
    public void Rollback_ShouldRevertUncommittedChanges()
    {
        // Arrange
        _root.IntValue = 0;

        // Act
        _root.IntValue = 100;
        _historyManager.Rollback();

        // Assert
        Assert.That(_root.IntValue, Is.EqualTo(0));
        Assert.That(_historyManager.CanUndo, Is.False);
    }

    [Test]
    public void Rollback_ShouldNotAffectCommittedChanges()
    {
        // Arrange
        _root.IntValue = 0;
        _root.IntValue = 50;
        _historyManager.Commit("First change");

        // Act
        _root.IntValue = 100;
        _historyManager.Rollback();

        // Assert
        Assert.That(_root.IntValue, Is.EqualTo(50));
        Assert.That(_historyManager.CanUndo, Is.True);
    }

    #endregion

    #region ExecuteInTransaction Tests

    [Test]
    public void ExecuteInTransaction_ShouldRecordAllChanges()
    {
        // Arrange - Set initial values
        _root.IntValue = 0;
        _root.StringValue = "Initial";
        _historyManager.Commit("Set initial");

        // Act
        _historyManager.ExecuteInTransaction(() =>
        {
            _root.IntValue = 100;
            _root.StringValue = "Modified";
        }, "Transaction");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(_root.IntValue, Is.EqualTo(100));
            Assert.That(_root.StringValue, Is.EqualTo("Modified"));
            Assert.That(_historyManager.UndoCount, Is.EqualTo(2));
        });

        // Undo
        _historyManager.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(_root.IntValue, Is.EqualTo(0));
            Assert.That(_root.StringValue, Is.EqualTo("Initial"));
        });
    }

    [Test]
    public void ExecuteInTransaction_ShouldRollbackOnException()
    {
        // Arrange
        _root.IntValue = 0;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            _historyManager.ExecuteInTransaction(() =>
            {
                _root.IntValue = 100;
                throw new InvalidOperationException("Test exception");
            }, "Transaction");
        });

        // Should be rolled back
        Assert.That(_root.IntValue, Is.EqualTo(0));
        Assert.That(_historyManager.CanUndo, Is.False);
    }

    #endregion

    #region State Notification Tests

    [Test]
    public void StateChanged_ShouldNotify_WhenPropertyChangesAreCommitted()
    {
        // Arrange
        List<HistoryState> states = new();
        using var subscription = _historyManager.StateChanged.Subscribe(s => states.Add(s));

        // Act
        _root.IntValue = 100;
        _historyManager.Commit("Change");

        // Assert
        Assert.That(states.Count, Is.GreaterThan(0));
        Assert.That(states[^1].CanUndo, Is.True);
    }

    [Test]
    public void StateChanged_ShouldNotify_AfterUndoRedo()
    {
        // Arrange
        _root.IntValue = 100;
        _historyManager.Commit("Change");

        List<HistoryState> states = new();
        using var subscription = _historyManager.StateChanged.Subscribe(s => states.Add(s));

        // Act
        _historyManager.Undo();
        _historyManager.Redo();

        // Assert
        Assert.That(states.Count, Is.EqualTo(2));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void NoChange_ShouldNotCreateTransaction()
    {
        // Act
        _historyManager.Commit("Empty");

        // Assert
        Assert.That(_historyManager.CanUndo, Is.False);
    }

    [Test]
    public void SameValueAssignment_ShouldNotCreateOperation()
    {
        // Arrange
        _root.IntValue = 100;
        _historyManager.Commit("Initial");
        var initialCount = _historyManager.UndoCount;

        // Act - Assign same value
        _root.IntValue = 100;
        _historyManager.Commit("Same value");

        // Assert - Count should remain the same (no new transaction for same value)
        Assert.That(_historyManager.UndoCount, Is.EqualTo(initialCount));
    }

    [Test]
    public void Clear_ShouldRemoveAllHistory_ButKeepCurrentState()
    {
        // Arrange
        _root.IntValue = 100;
        _historyManager.Commit("Change");

        // Act
        _historyManager.Clear();

        // Assert
        Assert.That(_historyManager.CanUndo, Is.False);
        Assert.That(_root.IntValue, Is.EqualTo(100)); // State is preserved
    }

    [Test]
    public void UndoRedo_ShouldNotTriggerNewOperations()
    {
        // Arrange
        _root.IntValue = 0;
        _root.IntValue = 100;
        _historyManager.Commit("Change");
        var undoCount = _historyManager.UndoCount;

        // Act
        _historyManager.Undo();
        _historyManager.Redo();

        // Assert - No new operations should be recorded from undo/redo
        Assert.That(_historyManager.UndoCount, Is.EqualTo(undoCount));
    }

    #endregion

    #region Complex Scenarios

    [Test]
    public void ComplexScenario_MultipleUndoRedoWithIntermediateChanges()
    {
        // Arrange & Act
        _root.IntValue = 0;

        _root.IntValue = 10;
        _historyManager.Commit("Step 1");

        _root.IntValue = 20;
        _historyManager.Commit("Step 2");

        _root.IntValue = 30;
        _historyManager.Commit("Step 3");

        // Undo twice
        _historyManager.Undo(); // 30 -> 20
        _historyManager.Undo(); // 20 -> 10

        Assert.That(_root.IntValue, Is.EqualTo(10));
        Assert.That(_historyManager.RedoCount, Is.EqualTo(2));

        // Make new change (should clear redo stack)
        _root.IntValue = 15;
        _historyManager.Commit("New branch");

        // Assert
        Assert.That(_root.IntValue, Is.EqualTo(15));
        Assert.That(_historyManager.RedoCount, Is.EqualTo(0));
        Assert.That(_historyManager.UndoCount, Is.EqualTo(2)); // Step 1 + New branch
    }

    [Test]
    public void ComplexScenario_NestedObjectWithMultipleProperties()
    {
        // Arrange
        var nested = new TestNestedModel { NestedIntValue = 0, NestedStringValue = "Initial" };
        _root.NestedObject = nested;
        _historyManager.Commit("Set nested");

        // Act
        nested.NestedIntValue = 100;
        nested.NestedStringValue = "Modified";
        _historyManager.Commit("Change nested properties");

        // Assert
        Assert.That(_historyManager.UndoCount, Is.EqualTo(2));

        // Undo nested changes
        _historyManager.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(nested.NestedIntValue, Is.EqualTo(0));
            Assert.That(nested.NestedStringValue, Is.EqualTo("Initial"));
        });

        // Redo
        _historyManager.Redo();
        Assert.Multiple(() =>
        {
            Assert.That(nested.NestedIntValue, Is.EqualTo(100));
            Assert.That(nested.NestedStringValue, Is.EqualTo("Modified"));
        });
    }

    #endregion

    #region Collection Operations Tests

    [Test]
    public void CollectionAdd_ShouldBeRecorded_AndUndoable()
    {
        // Arrange
        var modelWithList = new TestModelWithList();
        using var listObserver = new CoreObjectOperationObserver(null, modelWithList, _sequenceGenerator);
        _historyManager.Subscribe(listObserver);

        // Act
        modelWithList.Items.Add(new TestItem { Value = "Item1" });
        _historyManager.Commit("Add item");

        // Assert
        Assert.That(modelWithList.Items.Count, Is.EqualTo(1));
        Assert.That(_historyManager.CanUndo, Is.True);

        // Undo
        _historyManager.Undo();
        Assert.That(modelWithList.Items.Count, Is.EqualTo(0));
    }

    [Test]
    public void CollectionAddMultiple_ShouldBeRecorded_AndUndoable()
    {
        // Arrange
        var modelWithList = new TestModelWithList();
        using var listObserver = new CoreObjectOperationObserver(null, modelWithList, _sequenceGenerator);
        _historyManager.Subscribe(listObserver);

        // Act
        modelWithList.Items.Add(new TestItem { Value = "Item1" });
        modelWithList.Items.Add(new TestItem { Value = "Item2" });
        modelWithList.Items.Add(new TestItem { Value = "Item3" });
        _historyManager.Commit("Add items");

        // Assert
        Assert.That(modelWithList.Items.Count, Is.EqualTo(3));

        // Undo
        _historyManager.Undo();
        Assert.That(modelWithList.Items.Count, Is.EqualTo(0));
    }

    [Test]
    public void CollectionRemove_ShouldBeRecorded_AndUndoable()
    {
        // Arrange
        var modelWithList = new TestModelWithList();
        var item1 = new TestItem { Value = "Item1" };
        var item2 = new TestItem { Value = "Item2" };
        modelWithList.Items.Add(item1);
        modelWithList.Items.Add(item2);

        using var listObserver = new CoreObjectOperationObserver(null, modelWithList, _sequenceGenerator);
        _historyManager.Subscribe(listObserver);

        // Act
        modelWithList.Items.Remove(item1);
        _historyManager.Commit("Remove item");

        // Assert
        Assert.That(modelWithList.Items.Count, Is.EqualTo(1));
        Assert.That(modelWithList.Items[0].Value, Is.EqualTo("Item2"));

        // Undo
        _historyManager.Undo();
        Assert.That(modelWithList.Items.Count, Is.EqualTo(2));
        Assert.That(modelWithList.Items[0].Value, Is.EqualTo("Item1"));
    }

    [Test]
    public void CollectionInsertAt_ShouldBeRecorded_AndUndoable()
    {
        // Arrange
        var modelWithList = new TestModelWithList();
        modelWithList.Items.Add(new TestItem { Value = "Item1" });
        modelWithList.Items.Add(new TestItem { Value = "Item3" });

        using var listObserver = new CoreObjectOperationObserver(null, modelWithList, _sequenceGenerator);
        _historyManager.Subscribe(listObserver);

        // Act - Insert in the middle
        modelWithList.Items.Insert(1, new TestItem { Value = "Item2" });
        _historyManager.Commit("Insert item");

        // Assert
        Assert.That(modelWithList.Items.Count, Is.EqualTo(3));
        Assert.That(modelWithList.Items[1].Value, Is.EqualTo("Item2"));

        // Undo
        _historyManager.Undo();
        Assert.That(modelWithList.Items.Count, Is.EqualTo(2));
        Assert.That(modelWithList.Items[1].Value, Is.EqualTo("Item3"));
    }

    [Test]
    public void CollectionMove_ShouldBeRecorded_AndUndoable()
    {
        // Arrange
        var modelWithList = new TestModelWithList();
        modelWithList.Items.Add(new TestItem { Value = "Item1" });
        modelWithList.Items.Add(new TestItem { Value = "Item2" });
        modelWithList.Items.Add(new TestItem { Value = "Item3" });

        using var listObserver = new CoreObjectOperationObserver(null, modelWithList, _sequenceGenerator);
        _historyManager.Subscribe(listObserver);

        // Act - Move first item to last
        modelWithList.Items.Move(0, 2);
        _historyManager.Commit("Move item");

        // Assert
        Assert.That(modelWithList.Items[0].Value, Is.EqualTo("Item2"));
        Assert.That(modelWithList.Items[1].Value, Is.EqualTo("Item3"));
        Assert.That(modelWithList.Items[2].Value, Is.EqualTo("Item1"));

        // Undo
        _historyManager.Undo();
        Assert.That(modelWithList.Items[0].Value, Is.EqualTo("Item1"));
        Assert.That(modelWithList.Items[1].Value, Is.EqualTo("Item2"));
        Assert.That(modelWithList.Items[2].Value, Is.EqualTo("Item3"));
    }

    [Test]
    public void CollectionClear_ShouldBeRecorded_AndUndoable()
    {
        // Arrange
        var modelWithList = new TestModelWithList();
        // Set ResetBehavior to Remove so Clear() generates Remove operations instead of Reset
        modelWithList.Items.ResetBehavior = ResetBehavior.Remove;
        modelWithList.Items.Add(new TestItem { Value = "Item1" });
        modelWithList.Items.Add(new TestItem { Value = "Item2" });
        modelWithList.Items.Add(new TestItem { Value = "Item3" });

        using var listObserver = new CoreObjectOperationObserver(null, modelWithList, _sequenceGenerator);
        _historyManager.Subscribe(listObserver);

        // Act
        modelWithList.Items.Clear();
        _historyManager.Commit("Clear items");

        // Assert
        Assert.That(modelWithList.Items.Count, Is.EqualTo(0));

        // Undo
        _historyManager.Undo();
        Assert.That(modelWithList.Items.Count, Is.EqualTo(3));
    }

    [Test]
    public void CollectionItemPropertyChange_ShouldBeRecorded_AndUndoable()
    {
        // Arrange
        var modelWithList = new TestModelWithList();
        var item = new TestItem { Value = "Original" };
        modelWithList.Items.Add(item);

        using var listObserver = new CoreObjectOperationObserver(null, modelWithList, _sequenceGenerator);
        _historyManager.Subscribe(listObserver);
        _historyManager.Commit("Add item"); // Commit the add first

        // Act - Change property of item in collection
        item.Value = "Modified";
        _historyManager.Commit("Modify item");

        // Assert
        Assert.That(item.Value, Is.EqualTo("Modified"));

        // Undo
        _historyManager.Undo();
        Assert.That(item.Value, Is.EqualTo("Original"));
    }

    [Test]
    public void CollectionReplace_ShouldBeRecorded_AndUndoable()
    {
        // Arrange
        var modelWithList = new TestModelWithList();
        var item1 = new TestItem { Value = "Item1" };
        var item2 = new TestItem { Value = "NewItem" };
        modelWithList.Items.Add(item1);

        using var listObserver = new CoreObjectOperationObserver(null, modelWithList, _sequenceGenerator);
        _historyManager.Subscribe(listObserver);

        // Act - Replace item
        modelWithList.Items[0] = item2;
        _historyManager.Commit("Replace item");

        // Assert
        Assert.That(modelWithList.Items[0].Value, Is.EqualTo("NewItem"));

        // Undo
        _historyManager.Undo();
        Assert.That(modelWithList.Items[0].Value, Is.EqualTo("Item1"));
    }

    #endregion

    #region Test Models

    private class TestModel : CoreObject
    {
        public static readonly CoreProperty<int> IntValueProperty =
            ConfigureProperty<int, TestModel>(nameof(IntValue))
                .DefaultValue(0)
                .Register();

        public static readonly CoreProperty<string?> StringValueProperty =
            ConfigureProperty<string?, TestModel>(nameof(StringValue))
                .DefaultValue(null)
                .Register();

        public static readonly CoreProperty<double> DoubleValueProperty =
            ConfigureProperty<double, TestModel>(nameof(DoubleValue))
                .DefaultValue(0.0)
                .Register();

        public static readonly CoreProperty<int?> NullableValueProperty =
            ConfigureProperty<int?, TestModel>(nameof(NullableValue))
                .DefaultValue(null)
                .Register();

        public static readonly CoreProperty<TestNestedModel?> NestedObjectProperty =
            ConfigureProperty<TestNestedModel?, TestModel>(nameof(NestedObject))
                .DefaultValue(null)
                .Register();

        public int IntValue
        {
            get => GetValue(IntValueProperty);
            set => SetValue(IntValueProperty, value);
        }

        public string? StringValue
        {
            get => GetValue(StringValueProperty);
            set => SetValue(StringValueProperty, value);
        }

        public double DoubleValue
        {
            get => GetValue(DoubleValueProperty);
            set => SetValue(DoubleValueProperty, value);
        }

        public int? NullableValue
        {
            get => GetValue(NullableValueProperty);
            set => SetValue(NullableValueProperty, value);
        }

        public TestNestedModel? NestedObject
        {
            get => GetValue(NestedObjectProperty);
            set => SetValue(NestedObjectProperty, value);
        }
    }

    private class TestNestedModel : CoreObject
    {
        public static readonly CoreProperty<int> NestedIntValueProperty =
            ConfigureProperty<int, TestNestedModel>(nameof(NestedIntValue))
                .DefaultValue(0)
                .Register();

        public static readonly CoreProperty<string?> NestedStringValueProperty =
            ConfigureProperty<string?, TestNestedModel>(nameof(NestedStringValue))
                .DefaultValue(null)
                .Register();

        public int NestedIntValue
        {
            get => GetValue(NestedIntValueProperty);
            set => SetValue(NestedIntValueProperty, value);
        }

        public string? NestedStringValue
        {
            get => GetValue(NestedStringValueProperty);
            set => SetValue(NestedStringValueProperty, value);
        }
    }

    private class TestModelWithList : CoreObject
    {
        public static readonly CoreProperty<CoreList<TestItem>> ItemsProperty =
            ConfigureProperty<CoreList<TestItem>, TestModelWithList>(nameof(Items))
                .DefaultValue(null!)
                .Register();

        public TestModelWithList()
        {
            Items = new CoreList<TestItem>();
        }

        public CoreList<TestItem> Items
        {
            get => GetValue(ItemsProperty);
            set => SetValue(ItemsProperty, value);
        }
    }

    private class TestItem : CoreObject
    {
        public static readonly CoreProperty<string?> ValueProperty =
            ConfigureProperty<string?, TestItem>(nameof(Value))
                .DefaultValue(null)
                .Register();

        public string? Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }

    #endregion
}
