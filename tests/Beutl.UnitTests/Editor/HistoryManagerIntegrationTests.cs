using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.UnitTests.TestInfrastructure;
using static Beutl.Collections.ResetBehavior;

namespace Beutl.UnitTests.Editor;

/// <summary>Changes recorded through CoreObjectOperationObserver can be undone and redone.</summary>
public class HistoryManagerIntegrationTests
{
    private TestModel _root = null!;
    private HistoryHarness _harness = null!;
    private HistoryManager _historyManager = null!;

    [SetUp]
    public void Setup()
    {
        _root = new TestModel();
        _harness = new HistoryHarness(_root);
        _historyManager = _harness.History;
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    #region Simple Property Change Tests

    [Test]
    public void PropertyChange_ShouldBeRecorded_AndUndoable()
    {
        _root.IntValue = 0;

        _root.IntValue = 100;
        _historyManager.Commit("Change IntValue");

        Assert.That(_historyManager.CanUndo, Is.True);
        Assert.That(_root.IntValue, Is.EqualTo(100));

        _historyManager.Undo();
        Assert.That(_root.IntValue, Is.EqualTo(0));
    }

    [Test]
    public void PropertyChange_ShouldBeRedoable_AfterUndo()
    {
        _root.IntValue = 0;
        _root.IntValue = 100;
        _historyManager.Commit("Change IntValue");
        _historyManager.Undo();

        _historyManager.Redo();

        Assert.That(_root.IntValue, Is.EqualTo(100));
    }

    [Test]
    public void StringPropertyChange_ShouldBeRecorded_AndUndoable()
    {
        // Set initial value and commit to establish baseline
        _root.StringValue = "Initial";
        _historyManager.Commit("Set initial");

        _root.StringValue = "Modified";
        _historyManager.Commit("Change StringValue");

        Assert.That(_historyManager.CanUndo, Is.True);
        Assert.That(_root.StringValue, Is.EqualTo("Modified"));

        _historyManager.Undo();
        Assert.That(_root.StringValue, Is.EqualTo("Initial"));
    }

    [Test]
    public void NullablePropertyChange_ShouldBeRecorded_AndUndoable()
    {
        _root.NullableValue = null;

        _root.NullableValue = 42;
        _historyManager.Commit("Set NullableValue");

        Assert.That(_root.NullableValue, Is.EqualTo(42));

        _historyManager.Undo();
        Assert.That(_root.NullableValue, Is.Null);
    }

    #endregion

    #region Multiple Property Changes in Single Transaction

    [Test]
    public void MultiplePropertyChanges_InSingleTransaction_ShouldBeUndoneAtOnce()
    {
        // Set initial values and commit
        _root.IntValue = 0;
        _root.StringValue = "Initial";
        _root.DoubleValue = 0.0;
        _historyManager.Commit("Set initial values");

        _root.IntValue = 100;
        _root.StringValue = "Modified";
        _root.DoubleValue = 3.14;
        _historyManager.Commit("Multiple changes");

        Assert.That(_historyManager.UndoCount, Is.EqualTo(2));

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
        _root.IntValue = 0;

        _root.IntValue = 10;
        _historyManager.Commit("First");

        _root.IntValue = 20;
        _historyManager.Commit("Second");

        _root.IntValue = 30;
        _historyManager.Commit("Third");

        Assert.That(_historyManager.UndoCount, Is.EqualTo(3));
        Assert.That(_root.IntValue, Is.EqualTo(30));

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
        var nested = new TestNestedModel { NestedIntValue = 0 };
        _root.NestedObject = nested;
        _historyManager.Commit("Set nested object");

        nested.NestedIntValue = 999;
        _historyManager.Commit("Change nested property");

        Assert.That(_historyManager.UndoCount, Is.EqualTo(2));
        Assert.That(nested.NestedIntValue, Is.EqualTo(999));

        _historyManager.Undo();
        Assert.That(nested.NestedIntValue, Is.EqualTo(0));
    }

    [Test]
    public void NestedObjectReplacement_ShouldBeRecorded()
    {
        var nested1 = new TestNestedModel { NestedIntValue = 100 };
        var nested2 = new TestNestedModel { NestedIntValue = 200 };
        _root.NestedObject = nested1;
        _historyManager.Commit("Set first nested");

        _root.NestedObject = nested2;
        _historyManager.Commit("Replace nested");

        Assert.That(_root.NestedObject, Is.EqualTo(nested2));
        Assert.That(_root.NestedObject!.NestedIntValue, Is.EqualTo(200));

        _historyManager.Undo();
        Assert.That(_root.NestedObject, Is.EqualTo(nested1));
        Assert.That(_root.NestedObject!.NestedIntValue, Is.EqualTo(100));
    }

    #endregion

    #region Rollback Tests

    [Test]
    public void Rollback_ShouldRevertUncommittedChanges()
    {
        _root.IntValue = 0;

        _root.IntValue = 100;
        _historyManager.Rollback();

        Assert.That(_root.IntValue, Is.EqualTo(0));
        Assert.That(_historyManager.CanUndo, Is.False);
    }

    [Test]
    public void Rollback_ShouldNotAffectCommittedChanges()
    {
        _root.IntValue = 0;
        _root.IntValue = 50;
        _historyManager.Commit("First change");

        _root.IntValue = 100;
        _historyManager.Rollback();

        Assert.That(_root.IntValue, Is.EqualTo(50));
        Assert.That(_historyManager.CanUndo, Is.True);
    }

    #endregion

    #region ExecuteInTransaction Tests

    [Test]
    public void ExecuteInTransaction_ShouldRecordAllChanges()
    {
        // Set initial values
        _root.IntValue = 0;
        _root.StringValue = "Initial";
        _historyManager.Commit("Set initial");

        _historyManager.ExecuteInTransaction(() =>
        {
            _root.IntValue = 100;
            _root.StringValue = "Modified";
        }, "Transaction");

        Assert.Multiple(() =>
        {
            Assert.That(_root.IntValue, Is.EqualTo(100));
            Assert.That(_root.StringValue, Is.EqualTo("Modified"));
            Assert.That(_historyManager.UndoCount, Is.EqualTo(2));
        });

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
        _root.IntValue = 0;

        Assert.Throws<InvalidOperationException>(() =>
        {
            _historyManager.ExecuteInTransaction(() =>
            {
                _root.IntValue = 100;
                throw new InvalidOperationException("Test exception");
            }, "Transaction");
        });

        Assert.That(_root.IntValue, Is.EqualTo(0));
        Assert.That(_historyManager.CanUndo, Is.False);
    }

    #endregion

    #region State Notification Tests

    [Test]
    public void StateChanged_ShouldNotify_AfterUndoRedo()
    {
        _root.IntValue = 100;
        _historyManager.Commit("Change");

        List<HistoryState> states = new();
        using var subscription = _historyManager.StateChanged.Subscribe(s => states.Add(s));

        _historyManager.Undo();
        _historyManager.Redo();

        Assert.That(states.Count, Is.EqualTo(2));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void SameValueAssignment_ShouldNotCreateOperation()
    {
        _root.IntValue = 100;
        _historyManager.Commit("Initial");
        var initialCount = _historyManager.UndoCount;

        // Assign same value
        _root.IntValue = 100;
        _historyManager.Commit("Same value");

        Assert.That(_historyManager.UndoCount, Is.EqualTo(initialCount));
    }

    [Test]
    public void Clear_ShouldRemoveAllHistory_ButKeepCurrentState()
    {
        _root.IntValue = 100;
        _historyManager.Commit("Change");

        _historyManager.Clear();

        Assert.That(_historyManager.CanUndo, Is.False);
        Assert.That(_root.IntValue, Is.EqualTo(100));
    }

    [Test]
    public void UndoRedo_ShouldNotTriggerNewOperations()
    {
        _root.IntValue = 0;
        _root.IntValue = 100;
        _historyManager.Commit("Change");
        var undoCount = _historyManager.UndoCount;

        _historyManager.Undo();
        _historyManager.Redo();

        Assert.That(_historyManager.UndoCount, Is.EqualTo(undoCount));
    }

    #endregion

    #region Edits Returning to Their Starting Value

    // A drag in a number editor writes values as it moves and skips its own commit when it ends
    // where it began. The next commit, from any feature, must not turn those writes into an entry.
    [Test]
    public void EditReturningToStartingValue_LeavesNothingToCommit()
    {
        _root.IntValue = 5;
        _root.IntValue = 0;

        Assert.That(_historyManager.HasPendingOperations, Is.False);
        _historyManager.Commit("Export");
        Assert.That(_historyManager.CanUndo, Is.False);
    }

    [Test]
    public void EditReturningToStartingValue_KeepsRedoHistory()
    {
        _root.IntValue = 100;
        _historyManager.Commit("Change IntValue");
        _historyManager.Undo();

        _root.DoubleValue = 1.5;
        _root.DoubleValue = 0.0;
        _historyManager.Commit("Export");

        Assert.Multiple(() =>
        {
            Assert.That(_historyManager.CanUndo, Is.False);
            Assert.That(_historyManager.CanRedo, Is.True);
        });
        Assert.That(_historyManager.Redo(), Is.True);
        Assert.That(_root.IntValue, Is.EqualTo(100));
    }

    [Test]
    public void EditReturningToStartingValue_KeepsEditsRecordedInBetween()
    {
        _root.IntValue = 5;
        _root.StringValue = "Nudged";
        _root.DoubleValue = 2.0;
        _root.IntValue = 0;

        _historyManager.Commit("Nudge");
        Assert.That(_historyManager.UndoCount, Is.EqualTo(1));

        _historyManager.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(_root.StringValue, Is.Null);
            Assert.That(_root.DoubleValue, Is.EqualTo(0.0));
            Assert.That(_root.IntValue, Is.Zero);
        });

        _historyManager.Redo();
        Assert.Multiple(() =>
        {
            Assert.That(_root.StringValue, Is.EqualTo("Nudged"));
            Assert.That(_root.DoubleValue, Is.EqualTo(2.0));
            Assert.That(_root.IntValue, Is.Zero);
        });
    }

    [Test]
    public void EditPassingThroughStartingValue_RecordsTheFinalValue()
    {
        _root.IntValue = 5;
        _root.IntValue = 0;
        _root.IntValue = -3;
        _historyManager.Commit("Change IntValue");

        _historyManager.Undo();
        Assert.That(_root.IntValue, Is.Zero);
        _historyManager.Redo();
        Assert.That(_root.IntValue, Is.EqualTo(-3));
    }

    [Test]
    public void TextTypedBackToStartingValue_LeavesNothingToCommit()
    {
        _root.StringValue = "Title";
        _historyManager.Commit("Set title");

        _root.StringValue = "Titl";
        // A text box produces an equal string, not the same instance.
        _root.StringValue = new string("Title".AsSpan());

        Assert.That(_historyManager.HasPendingOperations, Is.False);
        _historyManager.Commit("Export");
        Assert.That(_historyManager.UndoCount, Is.EqualTo(1));
    }

    [Test]
    public void EnginePropertyReturningToStartingValue_LeavesNothingToCommit()
    {
        var model = new TestEngineModel();
        using var harness = new HistoryHarness(model);

        model.Value.Value = 5f;
        model.Value.Value = 0f;

        Assert.That(harness.History.HasPendingOperations, Is.False);
    }

    [Test]
    public void SplineHandleDraggedBackToStart_LeavesNothingToCommit()
    {
        var spline = new SplineEasing(0.25f, 0.1f, 0.25f, 1f);
        using var harness = new HistoryHarness(new KeyFrame<float> { Easing = spline });

        spline.X1 = 0.6f;
        spline.Y1 = 0.4f;
        spline.X1 = 0.25f;
        spline.Y1 = 0.1f;

        Assert.That(harness.History.HasPendingOperations, Is.False);
    }

    [Test]
    public void AnimationSetBackToTheSameInstance_LeavesNothingToCommit()
    {
        var model = new TestEngineModel();
        using var harness = new HistoryHarness(model);
        var original = new KeyFrameAnimation<float>();
        model.Value.SetKeyFrameAnimation(original);
        harness.History.Commit("Animate");

        model.Value.SetKeyFrameAnimation(new KeyFrameAnimation<float>());
        model.Value.SetKeyFrameAnimation(original);

        Assert.That(harness.History.HasPendingOperations, Is.False);
    }

    // An engine property replaces its expression by reference, so swapping in an equal expression
    // that is not the same instance is still a change that undo has to revert.
    [Test]
    public void ExpressionReplacedByEqualInstance_StaysUndoable()
    {
        var model = new TestEngineModel();
        using var harness = new HistoryHarness(model);
        var original = new EquatableExpression(1f);
        model.Value.Expression = original;
        harness.History.Commit("Set expression");

        model.Value.Expression = null;
        model.Value.Expression = new EquatableExpression(1f);
        Assert.That(harness.History.HasPendingOperations, Is.True);
        harness.History.Commit("Replace expression");

        Assert.That(harness.History.Undo(), Is.True);
        Assert.That(model.Value.Expression, Is.SameAs(original));
    }

    #endregion

    #region Complex Scenarios

    [Test]
    public void ComplexScenario_MultipleUndoRedoWithIntermediateChanges()
    {
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

        _root.IntValue = 15;
        _historyManager.Commit("New branch");

        Assert.That(_root.IntValue, Is.EqualTo(15));
        Assert.That(_historyManager.RedoCount, Is.EqualTo(0));
        Assert.That(_historyManager.UndoCount, Is.EqualTo(2)); // Step 1 + New branch
    }

    [Test]
    public void ComplexScenario_NestedObjectWithMultipleProperties()
    {
        var nested = new TestNestedModel { NestedIntValue = 0, NestedStringValue = "Initial" };
        _root.NestedObject = nested;
        _historyManager.Commit("Set nested");

        nested.NestedIntValue = 100;
        nested.NestedStringValue = "Modified";
        _historyManager.Commit("Change nested properties");

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
        var modelWithList = new TestModelWithList();
        using var harness = new HistoryHarness(modelWithList);

        modelWithList.Items.Add(new TestItem { Value = "Item1" });
        harness.History.Commit("Add item");

        Assert.That(modelWithList.Items.Count, Is.EqualTo(1));
        Assert.That(harness.History.CanUndo, Is.True);

        // Undo
        harness.History.Undo();
        Assert.That(modelWithList.Items.Count, Is.EqualTo(0));
    }

    [Test]
    public void CollectionAddMultiple_ShouldBeRecorded_AndUndoable()
    {
        var modelWithList = new TestModelWithList();
        using var harness = new HistoryHarness(modelWithList);

        modelWithList.Items.Add(new TestItem { Value = "Item1" });
        modelWithList.Items.Add(new TestItem { Value = "Item2" });
        modelWithList.Items.Add(new TestItem { Value = "Item3" });
        harness.History.Commit("Add items");

        Assert.That(modelWithList.Items.Count, Is.EqualTo(3));

        // Undo
        harness.History.Undo();
        Assert.That(modelWithList.Items.Count, Is.EqualTo(0));
    }

    [Test]
    public void CollectionRemove_ShouldBeRecorded_AndUndoable()
    {
        var modelWithList = new TestModelWithList();
        var item1 = new TestItem { Value = "Item1" };
        var item2 = new TestItem { Value = "Item2" };
        modelWithList.Items.Add(item1);
        modelWithList.Items.Add(item2);

        using var harness = new HistoryHarness(modelWithList);

        modelWithList.Items.Remove(item1);
        harness.History.Commit("Remove item");

        Assert.That(modelWithList.Items.Count, Is.EqualTo(1));
        Assert.That(modelWithList.Items[0].Value, Is.EqualTo("Item2"));

        // Undo
        harness.History.Undo();
        Assert.That(modelWithList.Items.Count, Is.EqualTo(2));
        Assert.That(modelWithList.Items[0].Value, Is.EqualTo("Item1"));
    }

    [Test]
    public void CollectionInsertAt_ShouldBeRecorded_AndUndoable()
    {
        var modelWithList = new TestModelWithList();
        modelWithList.Items.Add(new TestItem { Value = "Item1" });
        modelWithList.Items.Add(new TestItem { Value = "Item3" });

        using var harness = new HistoryHarness(modelWithList);

        modelWithList.Items.Insert(1, new TestItem { Value = "Item2" });
        harness.History.Commit("Insert item");

        Assert.That(modelWithList.Items.Count, Is.EqualTo(3));
        Assert.That(modelWithList.Items[1].Value, Is.EqualTo("Item2"));

        // Undo
        harness.History.Undo();
        Assert.That(modelWithList.Items.Count, Is.EqualTo(2));
        Assert.That(modelWithList.Items[1].Value, Is.EqualTo("Item3"));
    }

    [Test]
    public void CollectionMove_ShouldBeRecorded_AndUndoable()
    {
        var modelWithList = new TestModelWithList();
        modelWithList.Items.Add(new TestItem { Value = "Item1" });
        modelWithList.Items.Add(new TestItem { Value = "Item2" });
        modelWithList.Items.Add(new TestItem { Value = "Item3" });

        using var harness = new HistoryHarness(modelWithList);

        modelWithList.Items.Move(0, 2);
        harness.History.Commit("Move item");

        Assert.That(modelWithList.Items[0].Value, Is.EqualTo("Item2"));
        Assert.That(modelWithList.Items[1].Value, Is.EqualTo("Item3"));
        Assert.That(modelWithList.Items[2].Value, Is.EqualTo("Item1"));

        // Undo
        harness.History.Undo();
        Assert.That(modelWithList.Items[0].Value, Is.EqualTo("Item1"));
        Assert.That(modelWithList.Items[1].Value, Is.EqualTo("Item2"));
        Assert.That(modelWithList.Items[2].Value, Is.EqualTo("Item3"));
    }

    [Test]
    public void CollectionUndoRedo_FindsItemsThatAnUnrecordedChangeMoved()
    {
        var modelWithList = new TestModelWithList();
        var a = new TestItem { Value = "A" };
        var b = new TestItem { Value = "B" };
        var c = new TestItem { Value = "C" };
        modelWithList.Items.Add(a);
        modelWithList.Items.Add(b);
        using var harness = new HistoryHarness(modelWithList);

        modelWithList.Items.Insert(1, c);
        harness.History.Commit("Insert");
        modelWithList.Items.Move(0, 2);
        harness.History.Commit("Move");
        using (harness.History.SuppressRecording())
        {
            modelWithList.Items.Move(2, 0);
        }

        Assert.That(harness.History.Undo(), Is.True);
        Assert.That(modelWithList.Items, Is.EqualTo(new[] { a, c, b }));
        Assert.That(harness.History.Undo(), Is.True);
        Assert.That(modelWithList.Items, Is.EqualTo(new[] { a, b }));
        Assert.That(harness.History.Redo(), Is.True);
        Assert.That(harness.History.Redo(), Is.True);
        Assert.That(modelWithList.Items, Is.EqualTo(new[] { c, b, a }));
    }

    [Test]
    public void CollectionClear_ShouldBeRecorded_AndUndoable([Values] ResetBehavior resetBehavior)
    {
        var modelWithList = new TestModelWithList();
        // With ResetBehavior.Remove, Clear() notifies the removed items. With ResetBehavior.Reset
        // (the default), it notifies a Reset without them. Both must be recorded.
        modelWithList.Items.ResetBehavior = resetBehavior;
        modelWithList.Items.Add(new TestItem { Value = "Item1" });
        modelWithList.Items.Add(new TestItem { Value = "Item2" });
        modelWithList.Items.Add(new TestItem { Value = "Item3" });

        using var harness = new HistoryHarness(modelWithList);

        modelWithList.Items.Clear();
        harness.History.Commit("Clear items");

        Assert.That(modelWithList.Items.Count, Is.EqualTo(0));

        // Undo
        Assert.That(harness.History.Undo(), Is.True);
        Assert.That(modelWithList.Items.Select(item => item.Value), Is.EqualTo(new[] { "Item1", "Item2", "Item3" }));

        // Redo
        Assert.That(harness.History.Redo(), Is.True);
        Assert.That(modelWithList.Items, Is.Empty);
    }

    [Test]
    public void CollectionClearThenAdd_InOneTransaction_ShouldBeUndoable([Values] ResetBehavior resetBehavior)
    {
        // The agent's document applier rebuilds Scene.Groups this way
        var modelWithList = new TestModelWithList();
        modelWithList.Items.ResetBehavior = resetBehavior;
        var item1 = new TestItem { Value = "Item1" };
        var item2 = new TestItem { Value = "Item2" };
        var item3 = new TestItem { Value = "Item3" };
        modelWithList.Items.Add(item1);
        modelWithList.Items.Add(item2);

        using var harness = new HistoryHarness(modelWithList);

        // Item2 is cleared and added back
        harness.History.ExecuteInTransaction(() =>
        {
            modelWithList.Items.Clear();
            modelWithList.Items.Add(item2);
            modelWithList.Items.Add(item3);
        }, "Rebuild items");

        Assert.That(modelWithList.Items, Is.EqualTo(new[] { item2, item3 }));

        // Undo
        Assert.That(harness.History.Undo(), Is.True);
        Assert.That(modelWithList.Items, Is.EqualTo(new[] { item1, item2 }));

        // Redo
        Assert.That(harness.History.Redo(), Is.True);
        Assert.That(modelWithList.Items, Is.EqualTo(new[] { item2, item3 }));
    }

    [Test]
    public void CollectionClearThenAdd_ThatFails_ShouldBeRolledBack([Values] ResetBehavior resetBehavior)
    {
        var modelWithList = new TestModelWithList();
        modelWithList.Items.ResetBehavior = resetBehavior;
        var item1 = new TestItem { Value = "Item1" };
        var item2 = new TestItem { Value = "Item2" };
        modelWithList.Items.Add(item1);
        modelWithList.Items.Add(item2);

        using var harness = new HistoryHarness(modelWithList);

        Assert.Throws<InvalidOperationException>(() => harness.History.ExecuteInTransaction(() =>
        {
            modelWithList.Items.Clear();
            modelWithList.Items.Add(new TestItem { Value = "Item3" });
            throw new InvalidOperationException("Injected failure.");
        }, "Rebuild items"));

        // The rollback reverts the Clear as well as the Add
        Assert.That(modelWithList.Items, Is.EqualTo(new[] { item1, item2 }));
    }

    [Test]
    public void CollectionItemPropertyChange_ShouldBeRecorded_AndUndoable()
    {
        var modelWithList = new TestModelWithList();
        var item = new TestItem { Value = "Original" };
        modelWithList.Items.Add(item);

        using var harness = new HistoryHarness(modelWithList);
        harness.History.Commit("Add item");

        item.Value = "Modified";
        harness.History.Commit("Modify item");

        Assert.That(item.Value, Is.EqualTo("Modified"));

        // Undo
        harness.History.Undo();
        Assert.That(item.Value, Is.EqualTo("Original"));
    }

    [Test]
    public void CollectionReplace_ShouldBeRecorded_AndUndoable()
    {
        var modelWithList = new TestModelWithList();
        var item1 = new TestItem { Value = "Item1" };
        var item2 = new TestItem { Value = "NewItem" };
        modelWithList.Items.Add(item1);

        using var harness = new HistoryHarness(modelWithList);

        modelWithList.Items[0] = item2;
        harness.History.Commit("Replace item");

        Assert.That(modelWithList.Items[0].Value, Is.EqualTo("NewItem"));

        // Undo
        harness.History.Undo();
        Assert.That(modelWithList.Items[0].Value, Is.EqualTo("Item1"));
    }

    #endregion

    #region Test Models

    private class TestModel : CoreObject
    {
        public TestModel()
        {
            ScanProperties<TestModel>();
        }

        public IProperty<int> IntValueProperty { get; } = new ValueOnlyProperty<int>(0);

        public IProperty<string?> StringValueProperty { get; } = new ValueOnlyProperty<string?>(null);

        public IProperty<double> DoubleValueProperty { get; } = new ValueOnlyProperty<double>(0.0);

        public IProperty<int?> NullableValueProperty { get; } = new ValueOnlyProperty<int?>(null);

        public IProperty<TestNestedModel?> NestedObjectProperty { get; } = new ValueOnlyProperty<TestNestedModel?>(null);

        public int IntValue
        {
            get => IntValueProperty.Value;
            set => IntValueProperty.Value = value;
        }

        public string? StringValue
        {
            get => StringValueProperty.Value;
            set => StringValueProperty.Value = value;
        }

        public double DoubleValue
        {
            get => DoubleValueProperty.Value;
            set => DoubleValueProperty.Value = value;
        }

        public int? NullableValue
        {
            get => NullableValueProperty.Value;
            set => NullableValueProperty.Value = value;
        }

        public TestNestedModel? NestedObject
        {
            get => NestedObjectProperty.Value;
            set => NestedObjectProperty.Value = value;
        }
    }

    private class TestNestedModel : CoreObject
    {
        public TestNestedModel()
        {
            ScanProperties<TestNestedModel>();
        }

        public IProperty<int> NestedIntValueProperty { get; } = new ValueOnlyProperty<int>(0);

        public IProperty<string?> NestedStringValueProperty { get; } = new ValueOnlyProperty<string?>(null);

        public int NestedIntValue
        {
            get => NestedIntValueProperty.Value;
            set => NestedIntValueProperty.Value = value;
        }

        public string? NestedStringValue
        {
            get => NestedStringValueProperty.Value;
            set => NestedStringValueProperty.Value = value;
        }
    }

    private class TestModelWithList : CoreObject
    {
        public IProperty<CoreList<TestItem>> ItemsProperty { get; } = new ValueOnlyProperty<CoreList<TestItem>>(null!);

        public TestModelWithList()
        {
            ScanProperties<TestModelWithList>();
            Items = new CoreList<TestItem>();
        }

        public CoreList<TestItem> Items
        {
            get => ItemsProperty.Value;
            set => ItemsProperty.Value = value;
        }
    }

    private class TestItem : CoreObject
    {
        public TestItem()
        {
            ScanProperties<TestItem>();
        }

        public IProperty<string?> ValueProperty { get; } = new ValueOnlyProperty<string?>(null);

        public string? Value
        {
            get => ValueProperty.Value;
            set => ValueProperty.Value = value;
        }
    }

    [SuppressResourceClassGeneration]
    private class TestEngineModel : EngineObject
    {
        public TestEngineModel()
        {
            Value = Property.CreateAnimatable(0f);
            ScanProperties<TestEngineModel>();
        }

        public IExpressionProperty<float> Value { get; }
    }

    private sealed record EquatableExpression(float Value) : IExpression<float>
    {
        public string ExpressionString => string.Empty;

        public float Evaluate(ExpressionContext context) => Value;

        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }
    }

    #endregion
}
