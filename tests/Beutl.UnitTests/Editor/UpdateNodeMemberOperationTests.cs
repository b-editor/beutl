using Beutl.Animation;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Engine.Expressions;
using Beutl.Logging;
using Beutl.NodeGraph;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class UpdateNodeMemberOperationTests
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

    private sealed class TestNodeMember<T> : NodeMember<T>
    {
        public TestNodeMember(string name, T? value = default, IAnimation<T>? animation = null)
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

    private UpdateNodeMemberOperation CreateOp(
        INodeMember nodeMember,
        string propertyPath,
        object? newValue,
        object? oldValue)
    {
        return new UpdateNodeMemberOperation(nodeMember, propertyPath, newValue, oldValue)
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
    public void Constructor_ShouldSetNodeMember()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);

        var op = CreateOp(nodeMember, "Property", 20, 10);

        Assert.That(op.NodeMember, Is.SameAs(nodeMember));
    }

    [Test]
    public void Constructor_ShouldSetPropertyPath()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);

        var op = CreateOp(nodeMember, "Property", 20, 10);

        Assert.That(op.PropertyPath, Is.EqualTo("Property"));
    }

    [Test]
    public void Constructor_ShouldSetNewValue()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);

        var op = CreateOp(nodeMember, "Property", 20, 10);

        Assert.That(op.NewValue, Is.EqualTo(20));
    }

    [Test]
    public void Constructor_ShouldSetOldValue()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);

        var op = CreateOp(nodeMember, "Property", 20, 10);

        Assert.That(op.OldValue, Is.EqualTo(10));
    }

    [Test]
    public void Constructor_ShouldAcceptNullValues()
    {
        var nodeMember = new TestNodeMember<string>("TestProperty", "initial");

        var op = CreateOp(nodeMember, "Property", null, "initial");

        Assert.That(op.NewValue, Is.Null);
        Assert.That(op.OldValue, Is.EqualTo("initial"));
    }

    [Test]
    public void Constructor_ShouldAcceptComplexPropertyPath()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);

        var op = CreateOp(nodeMember, "Parent.Child.Property", 20, 10);

        Assert.That(op.PropertyPath, Is.EqualTo("Parent.Child.Property"));
    }

    #endregion

    #region Property Setter Tests

    [Test]
    public void NodeMember_SetterShouldUpdateValue()
    {
        var nodeMember1 = new TestNodeMember<int>("TestProperty1", 10);
        var nodeMember2 = new TestNodeMember<int>("TestProperty2", 20);

        var op = CreateOp(nodeMember1, "Property", 30, 10);
        op.NodeMember = nodeMember2;

        Assert.That(op.NodeMember, Is.SameAs(nodeMember2));
    }

    [Test]
    public void PropertyPath_SetterShouldUpdateValue()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);

        var op = CreateOp(nodeMember, "Property", 20, 10);
        op.PropertyPath = "Animation";

        Assert.That(op.PropertyPath, Is.EqualTo("Animation"));
    }

    [Test]
    public void NewValue_SetterShouldUpdateValue()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);

        var op = CreateOp(nodeMember, "Property", 20, 10);
        op.NewValue = 30;

        Assert.That(op.NewValue, Is.EqualTo(30));
    }

    [Test]
    public void OldValue_SetterShouldUpdateValue()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);

        var op = CreateOp(nodeMember, "Property", 20, 10);
        op.OldValue = 5;

        Assert.That(op.OldValue, Is.EqualTo(5));
    }

    [Test]
    public void NewValue_SetterShouldAcceptNull()
    {
        var nodeMember = new TestNodeMember<string>("TestProperty", "initial");

        var op = CreateOp(nodeMember, "Property", "new", "initial");
        op.NewValue = null;

        Assert.That(op.NewValue, Is.Null);
    }

    [Test]
    public void OldValue_SetterShouldAcceptNull()
    {
        var nodeMember = new TestNodeMember<string>("TestProperty", "initial");

        var op = CreateOp(nodeMember, "Property", "new", "initial");
        op.OldValue = null;

        Assert.That(op.OldValue, Is.Null);
    }

    #endregion

    #region Interface Implementation Tests

    [Test]
    public void ShouldImplementIPropertyPathProvider()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);

        var op = CreateOp(nodeMember, "Property", 20, 10);

        Assert.That(op, Is.InstanceOf<IPropertyPathProvider>());
        Assert.That(((IPropertyPathProvider)op).PropertyPath, Is.EqualTo("Property"));
    }

    [Test]
    public void ShouldImplementIMergableChangeOperation()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);

        var op = CreateOp(nodeMember, "Property", 20, 10);

        Assert.That(op, Is.InstanceOf<IMergableChangeOperation>());
    }

    [Test]
    public void ShouldInheritFromChangeOperation()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);

        var op = CreateOp(nodeMember, "Property", 20, 10);

        Assert.That(op, Is.InstanceOf<ChangeOperation>());
    }

    #endregion

    #region Apply Tests - Regular Property

    [Test]
    public void Apply_ShouldSetPropertyValue_WhenPropertyPathIsSimple()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op = CreateOp(nodeMember, "Property", 20, 10);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(20));
    }

    [Test]
    public void Apply_ShouldSetPropertyValue_WhenPropertyPathHasDot()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op = CreateOp(nodeMember, "Parent.Property", 30, 10);
        var context = CreateContext();

        op.Apply(context);

        // When PropertyPath contains a dot but doesn't end with Animation or Expression,
        // it should fall through to SetValue
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(30));
    }

    [Test]
    public void Apply_ShouldSetStringValue()
    {
        var nodeMember = new TestNodeMember<string>("TestProperty", "initial");
        var op = CreateOp(nodeMember, "Property", "updated", "initial");
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo("updated"));
    }

    [Test]
    public void Apply_ShouldSetNullValue()
    {
        var nodeMember = new TestNodeMember<string>("TestProperty", "initial");
        var op = CreateOp(nodeMember, "Property", null, "initial");
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.Null);
    }

    [Test]
    public void Apply_ShouldSetFloatValue()
    {
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.5f);
        var op = CreateOp(nodeMember, "Property", 2.5f, 1.5f);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(2.5f));
    }

    #endregion

    #region Apply Tests - Animation Property

    [Test]
    public void Apply_ShouldSetAnimation_WhenPropertyPathEndsWithAnimation()
    {
        var animation = new KeyFrameAnimation<float>();
        var newAnimation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, animation);
        var op = CreateOp(nodeMember, "Property.Animation", newAnimation, animation);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeMember.GetProperty()!.Animation, Is.SameAs(newAnimation));
    }

    [Test]
    public void Apply_ShouldSetAnimationToNull_WhenNewValueIsNull()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, animation);
        var op = CreateOp(nodeMember, "Property.Animation", null, animation);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeMember.GetProperty()!.Animation, Is.Null);
    }

    [Test]
    public void Apply_ShouldSetAnimationFromNull()
    {
        var newAnimation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, null);
        // PropertyPath needs a dot to trigger animation handling
        var op = CreateOp(nodeMember, "Property.Animation", newAnimation, null);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeMember.GetProperty()!.Animation, Is.SameAs(newAnimation));
    }

    [Test]
    public void Apply_ShouldIgnoreNonAnimation_WhenPropertyPathEndsWithAnimation()
    {
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, null);
        // Passing a non-animation value should result in null animation (due to "as IAnimation" cast)
        var op = CreateOp(nodeMember, "Property.Animation", "not-an-animation", null);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeMember.GetProperty()!.Animation, Is.Null);
    }

    #endregion

    #region Apply Tests - Expression Property

    [Test]
    public void Apply_ShouldNotSetExpression_WhenGraphNodePropertyAdapterDoesNotSupportExpressions()
    {
        // NodePropertyAdapter does not support expressions, so this should be a no-op
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f);
        var op = CreateOp(nodeMember, "Property.Expression", null, null);
        var context = CreateContext();

        // Should not throw, but also should not set anything (NodePropertyAdapter doesn't support expressions)
        Assert.DoesNotThrow(() => op.Apply(context));
    }

    #endregion

    #region Revert Tests

    [Test]
    public void Revert_ShouldRestoreOldValue()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op = CreateOp(nodeMember, "Property", 20, 10);
        var context = CreateContext();

        // First apply
        op.Apply(context);
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(20));

        // Then revert
        op.Revert(context);
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(10));
    }

    [Test]
    public void Revert_ShouldRestoreNullValue()
    {
        var nodeMember = new TestNodeMember<string>("TestProperty", null);
        nodeMember.SetPropertyValue("new");
        var op = CreateOp(nodeMember, "Property", "new", null);
        var context = CreateContext();

        op.Revert(context);

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.Null);
    }

    [Test]
    public void Revert_ShouldRestoreOldAnimation()
    {
        var oldAnimation = new KeyFrameAnimation<float>();
        var newAnimation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, oldAnimation);
        var op = CreateOp(nodeMember, "Property.Animation", newAnimation, oldAnimation);
        var context = CreateContext();

        // Apply new animation
        op.Apply(context);
        Assert.That(nodeMember.GetProperty()!.Animation, Is.SameAs(newAnimation));

        // Revert to old animation
        op.Revert(context);
        Assert.That(nodeMember.GetProperty()!.Animation, Is.SameAs(oldAnimation));
    }

    [Test]
    public void Revert_ShouldWorkWithoutPriorApply()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 20);
        var op = CreateOp(nodeMember, "Property", 20, 10);
        var context = CreateContext();

        op.Revert(context);

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(10));
    }

    [Test]
    public void Revert_ShouldWorkWithFloatValues()
    {
        var nodeMember = new TestNodeMember<float>("TestProperty", 2.5f);
        var op = CreateOp(nodeMember, "Property", 2.5f, 1.5f);
        var context = CreateContext();

        op.Revert(context);

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(1.5f));
    }

    #endregion

    #region TryMerge Tests

    [Test]
    public void TryMerge_ShouldReturnTrue_WhenSameNodeMemberAndPropertyPath()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op1 = CreateOp(nodeMember, "Property", 20, 10);
        var op2 = CreateOp(nodeMember, "Property", 30, 20);

        var result = op1.TryMerge(op2);

        Assert.That(result, Is.True);
    }

    [Test]
    public void TryMerge_ShouldUpdateNewValue_WhenMergeSucceeds()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op1 = CreateOp(nodeMember, "Property", 20, 10);
        var op2 = CreateOp(nodeMember, "Property", 30, 20);

        op1.TryMerge(op2);

        Assert.That(op1.NewValue, Is.EqualTo(30));
        Assert.That(op1.OldValue, Is.EqualTo(10)); // OldValue should remain unchanged
    }

    [Test]
    public void TryMerge_ShouldReturnFalse_WhenDifferentNodeMember()
    {
        var nodeMember1 = new TestNodeMember<int>("TestProperty1", 10);
        var nodeMember2 = new TestNodeMember<int>("TestProperty2", 10);
        var op1 = CreateOp(nodeMember1, "Property", 20, 10);
        var op2 = CreateOp(nodeMember2, "Property", 30, 10);

        var result = op1.TryMerge(op2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryMerge_ShouldReturnFalse_WhenDifferentPropertyPath()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op1 = CreateOp(nodeMember, "Property", 20, 10);
        var op2 = CreateOp(nodeMember, "Animation", 30, 10);

        var result = op1.TryMerge(op2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryMerge_ShouldReturnFalse_WhenOtherIsNotUpdateNodeMemberOperation()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op1 = CreateOp(nodeMember, "Property", 20, 10);
        var op2 = new TestChangeOperation { SequenceNumber = _sequenceGenerator.GetNext() };

        var result = op1.TryMerge(op2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryMerge_ShouldNotModifyOp1_WhenMergeFails()
    {
        var nodeMember1 = new TestNodeMember<int>("TestProperty1", 10);
        var nodeMember2 = new TestNodeMember<int>("TestProperty2", 10);
        var op1 = CreateOp(nodeMember1, "Property", 20, 10);
        var op2 = CreateOp(nodeMember2, "Property", 30, 10);

        op1.TryMerge(op2);

        Assert.That(op1.NewValue, Is.EqualTo(20));
        Assert.That(op1.OldValue, Is.EqualTo(10));
    }

    [Test]
    public void TryMerge_ShouldWorkWithNullValues()
    {
        var nodeMember = new TestNodeMember<string>("TestProperty", "initial");
        var op1 = CreateOp(nodeMember, "Property", "middle", "initial");
        var op2 = CreateOp(nodeMember, "Property", null, "middle");

        var result = op1.TryMerge(op2);

        Assert.That(result, Is.True);
        Assert.That(op1.NewValue, Is.Null);
        Assert.That(op1.OldValue, Is.EqualTo("initial"));
    }

    [Test]
    public void TryMerge_ShouldWorkWithAnimationPath()
    {
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f);
        var animation1 = new KeyFrameAnimation<float>();
        var animation2 = new KeyFrameAnimation<float>();
        var animation3 = new KeyFrameAnimation<float>();
        var op1 = CreateOp(nodeMember, "Property.Animation", animation2, animation1);
        var op2 = CreateOp(nodeMember, "Property.Animation", animation3, animation2);

        var result = op1.TryMerge(op2);

        Assert.That(result, Is.True);
        Assert.That(op1.NewValue, Is.SameAs(animation3));
        Assert.That(op1.OldValue, Is.SameAs(animation1));
    }

    [Test]
    public void TryMerge_MultipleMerges_ShouldAccumulateCorrectly()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 0);
        var op1 = CreateOp(nodeMember, "Property", 10, 0);
        var op2 = CreateOp(nodeMember, "Property", 20, 10);
        var op3 = CreateOp(nodeMember, "Property", 30, 20);

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
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);

        var op = CreateOp(nodeMember, "Property", 20, 10);

        Assert.That(op.SequenceNumber, Is.GreaterThan(0));
    }

    [Test]
    public void SequenceNumber_ShouldIncrementForEachOperation()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);

        var op1 = CreateOp(nodeMember, "Property", 20, 10);
        var op2 = CreateOp(nodeMember, "Property", 30, 20);
        var op3 = CreateOp(nodeMember, "Property", 40, 30);

        Assert.That(op1.SequenceNumber, Is.LessThan(op2.SequenceNumber));
        Assert.That(op2.SequenceNumber, Is.LessThan(op3.SequenceNumber));
    }

    #endregion

    #region Apply/Revert Cycle Tests

    [Test]
    public void ApplyRevertCycle_ShouldRestoreOriginalValue()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op = CreateOp(nodeMember, "Property", 20, 10);
        var context = CreateContext();

        var originalValue = nodeMember.GetProperty()!.GetValue();

        op.Apply(context);
        op.Revert(context);

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(originalValue));
    }

    [Test]
    public void ApplyRevertCycle_MultipleTimesWithSameValue()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op = CreateOp(nodeMember, "Property", 20, 10);
        var context = CreateContext();

        for (int i = 0; i < 5; i++)
        {
            op.Apply(context);
            Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(20));

            op.Revert(context);
            Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(10));
        }
    }

    [Test]
    public void ApplyRevertCycle_WithAnimation()
    {
        var oldAnimation = new KeyFrameAnimation<float>();
        var newAnimation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, oldAnimation);
        var op = CreateOp(nodeMember, "Property.Animation", newAnimation, oldAnimation);
        var context = CreateContext();

        op.Apply(context);
        Assert.That(nodeMember.GetProperty()!.Animation, Is.SameAs(newAnimation));

        op.Revert(context);
        Assert.That(nodeMember.GetProperty()!.Animation, Is.SameAs(oldAnimation));

        op.Apply(context);
        Assert.That(nodeMember.GetProperty()!.Animation, Is.SameAs(newAnimation));
    }

    [Test]
    public void ApplyRevertCycle_WithNullTransition()
    {
        var nodeMember = new TestNodeMember<string>("TestProperty", "initial");
        var op = CreateOp(nodeMember, "Property", null, "initial");
        var context = CreateContext();

        op.Apply(context);
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.Null);

        op.Revert(context);
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo("initial"));
    }

    [Test]
    public void ApplyRevertCycle_FromNullToValue()
    {
        var nodeMember = new TestNodeMember<string>("TestProperty", null);
        var op = CreateOp(nodeMember, "Property", "new-value", null);
        var context = CreateContext();

        op.Apply(context);
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo("new-value"));

        op.Revert(context);
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.Null);
    }

    [Test]
    public void ApplyRevertCycle_AnimationNullTransition()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, animation);
        var op = CreateOp(nodeMember, "Property.Animation", null, animation);
        var context = CreateContext();

        op.Apply(context);
        Assert.That(nodeMember.GetProperty()!.Animation, Is.Null);

        op.Revert(context);
        Assert.That(nodeMember.GetProperty()!.Animation, Is.SameAs(animation));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void Apply_ShouldHandleSameOldAndNewValue()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op = CreateOp(nodeMember, "Property", 10, 10);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(10));
    }

    [Test]
    public void Apply_ShouldHandleZeroValue()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op = CreateOp(nodeMember, "Property", 0, 10);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(0));
    }

    [Test]
    public void Apply_ShouldHandleNegativeValue()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op = CreateOp(nodeMember, "Property", -20, 10);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(-20));
    }

    [Test]
    public void Apply_ShouldHandleEmptyString()
    {
        var nodeMember = new TestNodeMember<string>("TestProperty", "initial");
        var op = CreateOp(nodeMember, "Property", "", "initial");
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(""));
    }

    [Test]
    public void Apply_ShouldHandleComplexObject()
    {
        var oldList = new List<int> { 1, 2, 3 };
        var newList = new List<int> { 4, 5, 6 };
        var nodeMember = new TestNodeMember<List<int>>("TestProperty", oldList);
        var op = CreateOp(nodeMember, "Property", newList, oldList);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.SameAs(newList));
    }

    [Test]
    public void PropertyPath_ShouldHandleNestedPath()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op = CreateOp(nodeMember, "Level1.Level2.Level3.Property", 20, 10);

        Assert.That(op.PropertyPath, Is.EqualTo("Level1.Level2.Level3.Property"));
    }

    #endregion

    #region Integration Tests

    [Test]
    public void UndoRedo_Integration_ShouldWorkCorrectly()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 0);
        var context = CreateContext();

        // Simulate a series of edits
        var op1 = CreateOp(nodeMember, "Property", 10, 0);
        var op2 = CreateOp(nodeMember, "Property", 20, 10);
        var op3 = CreateOp(nodeMember, "Property", 30, 20);

        // Apply all operations
        op1.Apply(context);
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(10));

        op2.Apply(context);
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(20));

        op3.Apply(context);
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(30));

        // Undo all operations
        op3.Revert(context);
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(20));

        op2.Revert(context);
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(10));

        op1.Revert(context);
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(0));
    }

    [Test]
    public void MergeAndApply_Integration_ShouldWorkCorrectly()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 0);
        var context = CreateContext();

        // Create multiple operations that can be merged
        var op1 = CreateOp(nodeMember, "Property", 10, 0);
        var op2 = CreateOp(nodeMember, "Property", 20, 10);
        var op3 = CreateOp(nodeMember, "Property", 30, 20);

        // Merge operations
        op1.TryMerge(op2);
        op1.TryMerge(op3);

        // Apply merged operation
        op1.Apply(context);
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(30));

        // Revert should go back to original value
        op1.Revert(context);
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(0));
    }

    [Test]
    public void MixedOperations_Integration()
    {
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f);
        var animation = new KeyFrameAnimation<float>();
        var context = CreateContext();

        // Property value change
        var valueOp = CreateOp(nodeMember, "Property", 2.0f, 1.0f);
        valueOp.Apply(context);
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(2.0f));

        // Animation change
        var animOp = CreateOp(nodeMember, "Property.Animation", animation, null);
        animOp.Apply(context);
        Assert.That(nodeMember.GetProperty()!.Animation, Is.SameAs(animation));

        // Revert animation
        animOp.Revert(context);
        Assert.That(nodeMember.GetProperty()!.Animation, Is.Null);

        // Value should still be 2.0f
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(2.0f));

        // Revert value
        valueOp.Revert(context);
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(1.0f));
    }

    #endregion

    #region PropertyPath Parsing Tests

    [Test]
    public void UpdateValue_ShouldRecognizeAnimationAtEnd()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f);
        var op = CreateOp(nodeMember, "Foo.Bar.Animation", animation, null);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeMember.GetProperty()!.Animation, Is.SameAs(animation));
    }

    [Test]
    public void UpdateValue_ShouldFallThroughToSetValue_WhenNotAnimationOrExpression()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op = CreateOp(nodeMember, "Foo.Bar.Value", 20, 10);
        var context = CreateContext();

        op.Apply(context);

        // Should call SetValue
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(20));
    }

    [Test]
    public void UpdateValue_ShouldHandleSingleSegmentPath()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op = CreateOp(nodeMember, "Property", 20, 10);
        var context = CreateContext();

        op.Apply(context);

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(20));
    }

    #endregion

    #region Null Property Tests

    [Test]
    public void Apply_ShouldNotThrow_WhenPropertyIsNull()
    {
        // Create a node item without setting up property
        var nodeMember = new DefaultNodeMember<int>();
        var op = CreateOp(nodeMember, "Property", 20, 10);
        var context = CreateContext();

        Assert.DoesNotThrow(() => op.Apply(context));
    }

    [Test]
    public void Revert_ShouldNotThrow_WhenPropertyIsNull()
    {
        var nodeMember = new DefaultNodeMember<int>();
        var op = CreateOp(nodeMember, "Property", 20, 10);
        var context = CreateContext();

        Assert.DoesNotThrow(() => op.Revert(context));
    }

    #endregion
}
