using Beutl.Animation;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Engine.Expressions;
using Beutl.Logging;
using Beutl.NodeTree;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class UpdateNodeItemOperationTests
{
    private OperationSequenceGenerator _sequenceGenerator = null!;

    [SetUp]
    public void SetUp()
    {
        _sequenceGenerator = new OperationSequenceGenerator();
        Log.LoggerFactory = LoggerFactory.Create(b => { });
    }

    [TearDown]
    public void TearDown()
    {
    }

    #region Helper Classes

    private sealed class TestNodeItem<T> : NodeItem<T>
    {
        public TestNodeItem(string name, T? value = default, IAnimation<T>? animation = null)
        {
            var adapter = new NodePropertyAdapter<T>(name, value, animation);
            Property = adapter;
        }

        public NodePropertyAdapter<T>? GetProperty() => Property as NodePropertyAdapter<T>;

        public void SetPropertyValue(T? value)
        {
            Property?.SetValue(value);
        }

        public void SetAnimation(IAnimation<T>? animation)
        {
            if (Property is NodePropertyAdapter<T> adapter)
            {
                adapter.Animation = animation;
            }
        }
    }

    private sealed class TestChangeOperation : ChangeOperation
    {
        public override void Apply(OperationExecutionContext context) { }
        public override void Revert(OperationExecutionContext context) { }
    }

    private sealed class TestCoreObject : CoreObject
    {
    }

    #endregion

    #region Helper Methods

    private UpdateNodeItemOperation CreateOp(
        INodeItem nodeItem,
        string propertyPath,
        object? newValue,
        object? oldValue)
    {
        return new UpdateNodeItemOperation(nodeItem, propertyPath, newValue, oldValue)
        {
            SequenceNumber = _sequenceGenerator.GetNext()
        };
    }

    private static OperationExecutionContext CreateContext()
    {
        return new OperationExecutionContext(new TestCoreObject());
    }

    #endregion

    #region Constructor Tests

    [Test]
    public void Constructor_ShouldSetNodeItem()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);

        var op = CreateOp(nodeItem, "Property", 20, 10);

        Assert.That(op.NodeItem, Is.SameAs(nodeItem));
    }

    [Test]
    public void Constructor_ShouldSetPropertyPath()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);

        var op = CreateOp(nodeItem, "Property", 20, 10);

        Assert.That(op.PropertyPath, Is.EqualTo("Property"));
    }

    [Test]
    public void Constructor_ShouldSetNewValue()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);

        var op = CreateOp(nodeItem, "Property", 20, 10);

        Assert.That(op.NewValue, Is.EqualTo(20));
    }

    [Test]
    public void Constructor_ShouldSetOldValue()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);

        var op = CreateOp(nodeItem, "Property", 20, 10);

        Assert.That(op.OldValue, Is.EqualTo(10));
    }

    [Test]
    public void Constructor_ShouldAcceptNullValues()
    {
        var nodeItem = new TestNodeItem<string>("TestProperty", "initial");

        var op = CreateOp(nodeItem, "Property", null, "initial");

        Assert.That(op.NewValue, Is.Null);
        Assert.That(op.OldValue, Is.EqualTo("initial"));
    }

    [Test]
    public void Constructor_ShouldAcceptComplexPropertyPath()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);

        var op = CreateOp(nodeItem, "Parent.Child.Property", 20, 10);

        Assert.That(op.PropertyPath, Is.EqualTo("Parent.Child.Property"));
    }

    #endregion

    #region Property Setter Tests

    [Test]
    public void NodeItem_SetterShouldUpdateValue()
    {
        var nodeItem1 = new TestNodeItem<int>("TestProperty1", 10);
        var nodeItem2 = new TestNodeItem<int>("TestProperty2", 20);

        var op = CreateOp(nodeItem1, "Property", 30, 10);
        op.NodeItem = nodeItem2;

        Assert.That(op.NodeItem, Is.SameAs(nodeItem2));
    }

    [Test]
    public void PropertyPath_SetterShouldUpdateValue()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);

        var op = CreateOp(nodeItem, "Property", 20, 10);
        op.PropertyPath = "Animation";

        Assert.That(op.PropertyPath, Is.EqualTo("Animation"));
    }

    [Test]
    public void NewValue_SetterShouldUpdateValue()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);

        var op = CreateOp(nodeItem, "Property", 20, 10);
        op.NewValue = 30;

        Assert.That(op.NewValue, Is.EqualTo(30));
    }

    [Test]
    public void OldValue_SetterShouldUpdateValue()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);

        var op = CreateOp(nodeItem, "Property", 20, 10);
        op.OldValue = 5;

        Assert.That(op.OldValue, Is.EqualTo(5));
    }

    [Test]
    public void NewValue_SetterShouldAcceptNull()
    {
        var nodeItem = new TestNodeItem<string>("TestProperty", "initial");

        var op = CreateOp(nodeItem, "Property", "new", "initial");
        op.NewValue = null;

        Assert.That(op.NewValue, Is.Null);
    }

    [Test]
    public void OldValue_SetterShouldAcceptNull()
    {
        var nodeItem = new TestNodeItem<string>("TestProperty", "initial");

        var op = CreateOp(nodeItem, "Property", "new", "initial");
        op.OldValue = null;

        Assert.That(op.OldValue, Is.Null);
    }

    #endregion

    #region Interface Implementation Tests

    [Test]
    public void ShouldImplementIPropertyPathProvider()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);

        var op = CreateOp(nodeItem, "Property", 20, 10);

        Assert.That(op, Is.InstanceOf<IPropertyPathProvider>());
        Assert.That(((IPropertyPathProvider)op).PropertyPath, Is.EqualTo("Property"));
    }

    [Test]
    public void ShouldImplementIMergableChangeOperation()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);

        var op = CreateOp(nodeItem, "Property", 20, 10);

        Assert.That(op, Is.InstanceOf<IMergableChangeOperation>());
    }

    [Test]
    public void ShouldInheritFromChangeOperation()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);

        var op = CreateOp(nodeItem, "Property", 20, 10);

        Assert.That(op, Is.InstanceOf<ChangeOperation>());
    }

    #endregion

    #region Apply Tests - Regular Property

    [Test]
    public void Apply_ShouldSetPropertyValue_WhenPropertyPathIsSimple()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);
        var op = CreateOp(nodeItem, "Property", 20, 10);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(20));
    }

    [Test]
    public void Apply_ShouldSetPropertyValue_WhenPropertyPathHasDot()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);
        var op = CreateOp(nodeItem, "Parent.Property", 30, 10);
        var context = CreateContext();

        op.Apply(context);

        // When PropertyPath contains a dot but doesn't end with Animation or Expression,
        // it should fall through to SetValue
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(30));
    }

    [Test]
    public void Apply_ShouldSetStringValue()
    {
        var nodeItem = new TestNodeItem<string>("TestProperty", "initial");
        var op = CreateOp(nodeItem, "Property", "updated", "initial");
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo("updated"));
    }

    [Test]
    public void Apply_ShouldSetNullValue()
    {
        var nodeItem = new TestNodeItem<string>("TestProperty", "initial");
        var op = CreateOp(nodeItem, "Property", null, "initial");
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.Null);
    }

    [Test]
    public void Apply_ShouldSetFloatValue()
    {
        var nodeItem = new TestNodeItem<float>("TestProperty", 1.5f);
        var op = CreateOp(nodeItem, "Property", 2.5f, 1.5f);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(2.5f));
    }

    #endregion

    #region Apply Tests - Animation Property

    [Test]
    public void Apply_ShouldSetAnimation_WhenPropertyPathEndsWithAnimation()
    {
        var animation = new KeyFrameAnimation<float>();
        var newAnimation = new KeyFrameAnimation<float>();
        var nodeItem = new TestNodeItem<float>("TestProperty", 1.0f, animation);
        var op = CreateOp(nodeItem, "Property.Animation", newAnimation, animation);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeItem.GetProperty()!.Animation, Is.SameAs(newAnimation));
    }

    [Test]
    public void Apply_ShouldSetAnimationToNull_WhenNewValueIsNull()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeItem = new TestNodeItem<float>("TestProperty", 1.0f, animation);
        var op = CreateOp(nodeItem, "Property.Animation", null, animation);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeItem.GetProperty()!.Animation, Is.Null);
    }

    [Test]
    public void Apply_ShouldSetAnimationFromNull()
    {
        var newAnimation = new KeyFrameAnimation<float>();
        var nodeItem = new TestNodeItem<float>("TestProperty", 1.0f, null);
        // PropertyPath needs a dot to trigger animation handling
        var op = CreateOp(nodeItem, "Property.Animation", newAnimation, null);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeItem.GetProperty()!.Animation, Is.SameAs(newAnimation));
    }

    [Test]
    public void Apply_ShouldIgnoreNonAnimation_WhenPropertyPathEndsWithAnimation()
    {
        var nodeItem = new TestNodeItem<float>("TestProperty", 1.0f, null);
        // Passing a non-animation value should result in null animation (due to "as IAnimation" cast)
        var op = CreateOp(nodeItem, "Property.Animation", "not-an-animation", null);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeItem.GetProperty()!.Animation, Is.Null);
    }

    #endregion

    #region Apply Tests - Expression Property

    [Test]
    public void Apply_ShouldNotSetExpression_WhenNodePropertyAdapterDoesNotSupportExpressions()
    {
        // NodePropertyAdapter does not support expressions, so this should be a no-op
        var nodeItem = new TestNodeItem<float>("TestProperty", 1.0f);
        var op = CreateOp(nodeItem, "Property.Expression", null, null);
        var context = CreateContext();

        // Should not throw, but also should not set anything (NodePropertyAdapter doesn't support expressions)
        Assert.DoesNotThrow(() => op.Apply(context));
    }

    #endregion

    #region Revert Tests

    [Test]
    public void Revert_ShouldRestoreOldValue()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);
        var op = CreateOp(nodeItem, "Property", 20, 10);
        var context = CreateContext();

        // First apply
        op.Apply(context);
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(20));

        // Then revert
        op.Revert(context);
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(10));
    }

    [Test]
    public void Revert_ShouldRestoreNullValue()
    {
        var nodeItem = new TestNodeItem<string>("TestProperty", null);
        nodeItem.SetPropertyValue("new");
        var op = CreateOp(nodeItem, "Property", "new", null);
        var context = CreateContext();

        op.Revert(context);

        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.Null);
    }

    [Test]
    public void Revert_ShouldRestoreOldAnimation()
    {
        var oldAnimation = new KeyFrameAnimation<float>();
        var newAnimation = new KeyFrameAnimation<float>();
        var nodeItem = new TestNodeItem<float>("TestProperty", 1.0f, oldAnimation);
        var op = CreateOp(nodeItem, "Property.Animation", newAnimation, oldAnimation);
        var context = CreateContext();

        // Apply new animation
        op.Apply(context);
        Assert.That(nodeItem.GetProperty()!.Animation, Is.SameAs(newAnimation));

        // Revert to old animation
        op.Revert(context);
        Assert.That(nodeItem.GetProperty()!.Animation, Is.SameAs(oldAnimation));
    }

    [Test]
    public void Revert_ShouldWorkWithoutPriorApply()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 20);
        var op = CreateOp(nodeItem, "Property", 20, 10);
        var context = CreateContext();

        op.Revert(context);

        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(10));
    }

    [Test]
    public void Revert_ShouldWorkWithFloatValues()
    {
        var nodeItem = new TestNodeItem<float>("TestProperty", 2.5f);
        var op = CreateOp(nodeItem, "Property", 2.5f, 1.5f);
        var context = CreateContext();

        op.Revert(context);

        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(1.5f));
    }

    #endregion

    #region TryMerge Tests

    [Test]
    public void TryMerge_ShouldReturnTrue_WhenSameNodeItemAndPropertyPath()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);
        var op1 = CreateOp(nodeItem, "Property", 20, 10);
        var op2 = CreateOp(nodeItem, "Property", 30, 20);

        var result = op1.TryMerge(op2);

        Assert.That(result, Is.True);
    }

    [Test]
    public void TryMerge_ShouldUpdateNewValue_WhenMergeSucceeds()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);
        var op1 = CreateOp(nodeItem, "Property", 20, 10);
        var op2 = CreateOp(nodeItem, "Property", 30, 20);

        op1.TryMerge(op2);

        Assert.That(op1.NewValue, Is.EqualTo(30));
        Assert.That(op1.OldValue, Is.EqualTo(10)); // OldValue should remain unchanged
    }

    [Test]
    public void TryMerge_ShouldReturnFalse_WhenDifferentNodeItem()
    {
        var nodeItem1 = new TestNodeItem<int>("TestProperty1", 10);
        var nodeItem2 = new TestNodeItem<int>("TestProperty2", 10);
        var op1 = CreateOp(nodeItem1, "Property", 20, 10);
        var op2 = CreateOp(nodeItem2, "Property", 30, 10);

        var result = op1.TryMerge(op2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryMerge_ShouldReturnFalse_WhenDifferentPropertyPath()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);
        var op1 = CreateOp(nodeItem, "Property", 20, 10);
        var op2 = CreateOp(nodeItem, "Animation", 30, 10);

        var result = op1.TryMerge(op2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryMerge_ShouldReturnFalse_WhenOtherIsNotUpdateNodeItemOperation()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);
        var op1 = CreateOp(nodeItem, "Property", 20, 10);
        var op2 = new TestChangeOperation { SequenceNumber = _sequenceGenerator.GetNext() };

        var result = op1.TryMerge(op2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryMerge_ShouldNotModifyOp1_WhenMergeFails()
    {
        var nodeItem1 = new TestNodeItem<int>("TestProperty1", 10);
        var nodeItem2 = new TestNodeItem<int>("TestProperty2", 10);
        var op1 = CreateOp(nodeItem1, "Property", 20, 10);
        var op2 = CreateOp(nodeItem2, "Property", 30, 10);

        op1.TryMerge(op2);

        Assert.That(op1.NewValue, Is.EqualTo(20));
        Assert.That(op1.OldValue, Is.EqualTo(10));
    }

    [Test]
    public void TryMerge_ShouldWorkWithNullValues()
    {
        var nodeItem = new TestNodeItem<string>("TestProperty", "initial");
        var op1 = CreateOp(nodeItem, "Property", "middle", "initial");
        var op2 = CreateOp(nodeItem, "Property", null, "middle");

        var result = op1.TryMerge(op2);

        Assert.That(result, Is.True);
        Assert.That(op1.NewValue, Is.Null);
        Assert.That(op1.OldValue, Is.EqualTo("initial"));
    }

    [Test]
    public void TryMerge_ShouldWorkWithAnimationPath()
    {
        var nodeItem = new TestNodeItem<float>("TestProperty", 1.0f);
        var animation1 = new KeyFrameAnimation<float>();
        var animation2 = new KeyFrameAnimation<float>();
        var animation3 = new KeyFrameAnimation<float>();
        var op1 = CreateOp(nodeItem, "Property.Animation", animation2, animation1);
        var op2 = CreateOp(nodeItem, "Property.Animation", animation3, animation2);

        var result = op1.TryMerge(op2);

        Assert.That(result, Is.True);
        Assert.That(op1.NewValue, Is.SameAs(animation3));
        Assert.That(op1.OldValue, Is.SameAs(animation1));
    }

    [Test]
    public void TryMerge_MultipleMerges_ShouldAccumulateCorrectly()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 0);
        var op1 = CreateOp(nodeItem, "Property", 10, 0);
        var op2 = CreateOp(nodeItem, "Property", 20, 10);
        var op3 = CreateOp(nodeItem, "Property", 30, 20);

        op1.TryMerge(op2);
        op1.TryMerge(op3);

        Assert.That(op1.NewValue, Is.EqualTo(30));
        Assert.That(op1.OldValue, Is.EqualTo(0));
    }

    #endregion

    #region SequenceNumber Tests

    [Test]
    public void SequenceNumber_ShouldBeSetByConstructor()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);

        var op = CreateOp(nodeItem, "Property", 20, 10);

        Assert.That(op.SequenceNumber, Is.GreaterThan(0));
    }

    [Test]
    public void SequenceNumber_ShouldIncrementForEachOperation()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);

        var op1 = CreateOp(nodeItem, "Property", 20, 10);
        var op2 = CreateOp(nodeItem, "Property", 30, 20);
        var op3 = CreateOp(nodeItem, "Property", 40, 30);

        Assert.That(op1.SequenceNumber, Is.LessThan(op2.SequenceNumber));
        Assert.That(op2.SequenceNumber, Is.LessThan(op3.SequenceNumber));
    }

    #endregion

    #region Apply/Revert Cycle Tests

    [Test]
    public void ApplyRevertCycle_ShouldRestoreOriginalValue()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);
        var op = CreateOp(nodeItem, "Property", 20, 10);
        var context = CreateContext();

        var originalValue = nodeItem.GetProperty()!.GetValue();

        op.Apply(context);
        op.Revert(context);

        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(originalValue));
    }

    [Test]
    public void ApplyRevertCycle_MultipleTimesWithSameValue()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);
        var op = CreateOp(nodeItem, "Property", 20, 10);
        var context = CreateContext();

        for (int i = 0; i < 5; i++)
        {
            op.Apply(context);
            Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(20));

            op.Revert(context);
            Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(10));
        }
    }

    [Test]
    public void ApplyRevertCycle_WithAnimation()
    {
        var oldAnimation = new KeyFrameAnimation<float>();
        var newAnimation = new KeyFrameAnimation<float>();
        var nodeItem = new TestNodeItem<float>("TestProperty", 1.0f, oldAnimation);
        var op = CreateOp(nodeItem, "Property.Animation", newAnimation, oldAnimation);
        var context = CreateContext();

        op.Apply(context);
        Assert.That(nodeItem.GetProperty()!.Animation, Is.SameAs(newAnimation));

        op.Revert(context);
        Assert.That(nodeItem.GetProperty()!.Animation, Is.SameAs(oldAnimation));

        op.Apply(context);
        Assert.That(nodeItem.GetProperty()!.Animation, Is.SameAs(newAnimation));
    }

    [Test]
    public void ApplyRevertCycle_WithNullTransition()
    {
        var nodeItem = new TestNodeItem<string>("TestProperty", "initial");
        var op = CreateOp(nodeItem, "Property", null, "initial");
        var context = CreateContext();

        op.Apply(context);
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.Null);

        op.Revert(context);
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo("initial"));
    }

    [Test]
    public void ApplyRevertCycle_FromNullToValue()
    {
        var nodeItem = new TestNodeItem<string>("TestProperty", null);
        var op = CreateOp(nodeItem, "Property", "new-value", null);
        var context = CreateContext();

        op.Apply(context);
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo("new-value"));

        op.Revert(context);
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.Null);
    }

    [Test]
    public void ApplyRevertCycle_AnimationNullTransition()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeItem = new TestNodeItem<float>("TestProperty", 1.0f, animation);
        var op = CreateOp(nodeItem, "Property.Animation", null, animation);
        var context = CreateContext();

        op.Apply(context);
        Assert.That(nodeItem.GetProperty()!.Animation, Is.Null);

        op.Revert(context);
        Assert.That(nodeItem.GetProperty()!.Animation, Is.SameAs(animation));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void Apply_ShouldHandleSameOldAndNewValue()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);
        var op = CreateOp(nodeItem, "Property", 10, 10);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(10));
    }

    [Test]
    public void Apply_ShouldHandleZeroValue()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);
        var op = CreateOp(nodeItem, "Property", 0, 10);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(0));
    }

    [Test]
    public void Apply_ShouldHandleNegativeValue()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);
        var op = CreateOp(nodeItem, "Property", -20, 10);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(-20));
    }

    [Test]
    public void Apply_ShouldHandleEmptyString()
    {
        var nodeItem = new TestNodeItem<string>("TestProperty", "initial");
        var op = CreateOp(nodeItem, "Property", "", "initial");
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(""));
    }

    [Test]
    public void Apply_ShouldHandleComplexObject()
    {
        var oldList = new List<int> { 1, 2, 3 };
        var newList = new List<int> { 4, 5, 6 };
        var nodeItem = new TestNodeItem<List<int>>("TestProperty", oldList);
        var op = CreateOp(nodeItem, "Property", newList, oldList);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.SameAs(newList));
    }

    [Test]
    public void PropertyPath_ShouldHandleNestedPath()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);
        var op = CreateOp(nodeItem, "Level1.Level2.Level3.Property", 20, 10);

        Assert.That(op.PropertyPath, Is.EqualTo("Level1.Level2.Level3.Property"));
    }

    #endregion

    #region Integration Tests

    [Test]
    public void UndoRedo_Integration_ShouldWorkCorrectly()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 0);
        var context = CreateContext();

        // Simulate a series of edits
        var op1 = CreateOp(nodeItem, "Property", 10, 0);
        var op2 = CreateOp(nodeItem, "Property", 20, 10);
        var op3 = CreateOp(nodeItem, "Property", 30, 20);

        // Apply all operations
        op1.Apply(context);
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(10));

        op2.Apply(context);
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(20));

        op3.Apply(context);
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(30));

        // Undo all operations
        op3.Revert(context);
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(20));

        op2.Revert(context);
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(10));

        op1.Revert(context);
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(0));
    }

    [Test]
    public void MergeAndApply_Integration_ShouldWorkCorrectly()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 0);
        var context = CreateContext();

        // Create multiple operations that can be merged
        var op1 = CreateOp(nodeItem, "Property", 10, 0);
        var op2 = CreateOp(nodeItem, "Property", 20, 10);
        var op3 = CreateOp(nodeItem, "Property", 30, 20);

        // Merge operations
        op1.TryMerge(op2);
        op1.TryMerge(op3);

        // Apply merged operation
        op1.Apply(context);
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(30));

        // Revert should go back to original value
        op1.Revert(context);
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(0));
    }

    [Test]
    public void MixedOperations_Integration()
    {
        var nodeItem = new TestNodeItem<float>("TestProperty", 1.0f);
        var animation = new KeyFrameAnimation<float>();
        var context = CreateContext();

        // Property value change
        var valueOp = CreateOp(nodeItem, "Property", 2.0f, 1.0f);
        valueOp.Apply(context);
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(2.0f));

        // Animation change
        var animOp = CreateOp(nodeItem, "Property.Animation", animation, null);
        animOp.Apply(context);
        Assert.That(nodeItem.GetProperty()!.Animation, Is.SameAs(animation));

        // Revert animation
        animOp.Revert(context);
        Assert.That(nodeItem.GetProperty()!.Animation, Is.Null);

        // Value should still be 2.0f
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(2.0f));

        // Revert value
        valueOp.Revert(context);
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(1.0f));
    }

    #endregion

    #region PropertyPath Parsing Tests

    [Test]
    public void UpdateValue_ShouldRecognizeAnimationAtEnd()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeItem = new TestNodeItem<float>("TestProperty", 1.0f);
        var op = CreateOp(nodeItem, "Foo.Bar.Animation", animation, null);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeItem.GetProperty()!.Animation, Is.SameAs(animation));
    }

    [Test]
    public void UpdateValue_ShouldFallThroughToSetValue_WhenNotAnimationOrExpression()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);
        var op = CreateOp(nodeItem, "Foo.Bar.Value", 20, 10);
        var context = CreateContext();

        op.Apply(context);

        // Should call SetValue
        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(20));
    }

    [Test]
    public void UpdateValue_ShouldHandleSingleSegmentPath()
    {
        var nodeItem = new TestNodeItem<int>("TestProperty", 10);
        var op = CreateOp(nodeItem, "Property", 20, 10);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeItem.GetProperty()!.GetValue(), Is.EqualTo(20));
    }

    #endregion

    #region Null Property Tests

    [Test]
    public void Apply_ShouldNotThrow_WhenPropertyIsNull()
    {
        // Create a node item without setting up property
        var nodeItem = new DefaultNodeItem<int>();
        var op = CreateOp(nodeItem, "Property", 20, 10);
        var context = CreateContext();

        Assert.DoesNotThrow(() => op.Apply(context));
    }

    [Test]
    public void Revert_ShouldNotThrow_WhenPropertyIsNull()
    {
        var nodeItem = new DefaultNodeItem<int>();
        var op = CreateOp(nodeItem, "Property", 20, 10);
        var context = CreateContext();

        Assert.DoesNotThrow(() => op.Revert(context));
    }

    #endregion
}
