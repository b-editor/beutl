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

    #region Helper Classes

    private sealed class TestNodeMember<T> : NodeMember<T>
    {
        public TestNodeMember(string name, T? value = default, IAnimation<T>? animation = null)
        {
            var adapter = new NodePropertyAdapter<T>(name, value, animation);
            Property = adapter;
        }

        public NodePropertyAdapter<T>? GetProperty() => Property as NodePropertyAdapter<T>;
    }

    private sealed class TestChangeOperation : ChangeOperation
    {
        public override void Apply() { }
        public override void Revert() { }
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

    #endregion

    #region Constructor Tests

    [Test]
    public void Constructor_ShouldSetNodeMemberPathAndValues()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);

        var op = CreateOp(nodeMember, "Property", 20, 10);

        Assert.Multiple(() =>
        {
            Assert.That(op.NodeMember, Is.SameAs(nodeMember));
            Assert.That(op.PropertyPath, Is.EqualTo("Property"));
            Assert.That(op.NewValue, Is.EqualTo(20));
            Assert.That(op.OldValue, Is.EqualTo(10));
        });
    }

    #endregion

    #region Apply Tests

    [TestCase("Property")]
    [TestCase("Parent.Property")]
    [TestCase("Foo.Bar.Value")]
    public void Apply_ShouldSetPropertyValue_WhenPathDoesNotEndWithAnimationOrExpression(string propertyPath)
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op = CreateOp(nodeMember, propertyPath, 20, 10);

        op.Apply();

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(20));
    }

    [TestCase("Property.Animation")]
    [TestCase("Foo.Bar.Animation")]
    public void Apply_ShouldSetAnimation_WhenPathEndsWithAnimation(string propertyPath)
    {
        var newAnimation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, null);
        var op = CreateOp(nodeMember, propertyPath, newAnimation, null);

        op.Apply();

        Assert.That(nodeMember.GetProperty()!.Animation, Is.SameAs(newAnimation));
    }

    [Test]
    public void Apply_ShouldIgnoreNonAnimation_WhenPropertyPathEndsWithAnimation()
    {
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, null);
        // Passing a non-animation value should result in null animation (due to "as IAnimation" cast)
        var op = CreateOp(nodeMember, "Property.Animation", "not-an-animation", null);

        op.Apply();

        Assert.That(nodeMember.GetProperty()!.Animation, Is.Null);
    }

    [Test]
    public void Apply_ShouldNotSetExpression_WhenGraphNodePropertyAdapterDoesNotSupportExpressions()
    {
        // NodePropertyAdapter does not support expressions, so this should be a no-op
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f);
        var op = CreateOp(nodeMember, "Property.Expression", null, null);

        Assert.DoesNotThrow(() => op.Apply());
    }

    #endregion

    #region Revert Tests

    [Test]
    public void Revert_ShouldWorkWithoutPriorApply()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 20);
        var op = CreateOp(nodeMember, "Property", 20, 10);

        op.Revert();

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(10));
    }

    [Test]
    public void Revert_ShouldWorkWithFloatValues()
    {
        var nodeMember = new TestNodeMember<float>("TestProperty", 2.5f);
        var op = CreateOp(nodeMember, "Property", 2.5f, 1.5f);

        op.Revert();

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(1.5f));
    }

    #endregion

    #region TryMerge Tests

    [Test]
    public void TryMerge_ShouldUpdateNewValue_WhenSameNodeMemberAndPropertyPath()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op1 = CreateOp(nodeMember, "Property", 20, 10);
        var op2 = CreateOp(nodeMember, "Property", 30, 20);

        var result = op1.TryMerge(op2);

        Assert.That(result, Is.True);
        Assert.That(op1.NewValue, Is.EqualTo(30));
        Assert.That(op1.OldValue, Is.EqualTo(10));
    }

    [Test]
    public void TryMerge_ShouldReturnFalseAndKeepValues_WhenDifferentNodeMember()
    {
        var nodeMember1 = new TestNodeMember<int>("TestProperty1", 10);
        var nodeMember2 = new TestNodeMember<int>("TestProperty2", 10);
        var op1 = CreateOp(nodeMember1, "Property", 20, 10);
        var op2 = CreateOp(nodeMember2, "Property", 30, 10);

        var result = op1.TryMerge(op2);

        Assert.That(result, Is.False);
        Assert.That(op1.NewValue, Is.EqualTo(20));
        Assert.That(op1.OldValue, Is.EqualTo(10));
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

    #endregion

    #region IsNoop Tests

    [Test]
    public void IsNoop_IsTrue_WhenMergedWritesReturnToTheOldValue()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op = CreateOp(nodeMember, "Property", 20, 10);

        op.TryMerge(CreateOp(nodeMember, "Property", 10, 20));

        // Each 10 is boxed separately, so only an equality check sees them as the same value.
        Assert.That(op.IsNoop, Is.True);
    }

    [Test]
    public void IsNoop_IsFalse_WhileTheValueDiffers()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op = CreateOp(nodeMember, "Property", 20, 10);

        Assert.That(op.IsNoop, Is.False);
    }

    // The adapters replace an animation or expression by reference, so an equal one that is not the
    // same instance is still a change.
    [TestCase("Property.Animation")]
    [TestCase("Property.Expression")]
    public void IsNoop_ComparesAnimationsAndExpressionsByReference(string propertyPath)
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op = CreateOp(nodeMember, propertyPath, 10, 10);

        Assert.That(op.IsNoop, Is.False);
    }

    private static IEnumerable<TestCaseData> AnimationAndExpressionReplacements()
    {
        yield return new TestCaseData(
                "Property.Animation", new KeyFrameAnimation<float>(), new KeyFrameAnimation<float>())
            .SetArgDisplayNames("Animation");
        yield return new TestCaseData(
                "Property.Expression", new StringExpression<float>("1"), new StringExpression<float>("1"))
            .SetArgDisplayNames("Expression");
    }

    [TestCaseSource(nameof(AnimationAndExpressionReplacements))]
    public void IsNoop_IsTrue_WhenAnAnimationOrExpressionReturnsToTheSameInstance(
        string propertyPath, object original, object replacement)
    {
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f);
        var op = CreateOp(nodeMember, propertyPath, replacement, original);

        op.TryMerge(CreateOp(nodeMember, propertyPath, original, replacement));

        Assert.That(op.IsNoop, Is.True);
    }

    #endregion

    #region Apply/Revert Cycle Tests

    [Test]
    public void ApplyRevertCycle_ShouldRestoreOriginalValue()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op = CreateOp(nodeMember, "Property", 20, 10);

        var originalValue = nodeMember.GetProperty()!.GetValue();

        op.Apply();
        op.Revert();

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(originalValue));
    }

    [Test]
    public void ApplyRevertCycle_MultipleTimesWithSameValue()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 10);
        var op = CreateOp(nodeMember, "Property", 20, 10);

        for (int i = 0; i < 5; i++)
        {
            op.Apply();
            Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(20));

            op.Revert();
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

        op.Apply();
        Assert.That(nodeMember.GetProperty()!.Animation, Is.SameAs(newAnimation));

        op.Revert();
        Assert.That(nodeMember.GetProperty()!.Animation, Is.SameAs(oldAnimation));

        op.Apply();
        Assert.That(nodeMember.GetProperty()!.Animation, Is.SameAs(newAnimation));
    }

    [Test]
    public void ApplyRevertCycle_WithNullTransition()
    {
        var nodeMember = new TestNodeMember<string>("TestProperty", "initial");
        var op = CreateOp(nodeMember, "Property", null, "initial");

        op.Apply();
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.Null);

        op.Revert();
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo("initial"));
    }

    [Test]
    public void ApplyRevertCycle_FromNullToValue()
    {
        var nodeMember = new TestNodeMember<string>("TestProperty", null);
        var op = CreateOp(nodeMember, "Property", "new-value", null);

        op.Apply();
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo("new-value"));

        op.Revert();
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.Null);
    }

    [Test]
    public void ApplyRevertCycle_AnimationNullTransition()
    {
        var animation = new KeyFrameAnimation<float>();
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f, animation);
        var op = CreateOp(nodeMember, "Property.Animation", null, animation);

        op.Apply();
        Assert.That(nodeMember.GetProperty()!.Animation, Is.Null);

        op.Revert();
        Assert.That(nodeMember.GetProperty()!.Animation, Is.SameAs(animation));
    }

    #endregion

    #region Integration Tests

    [Test]
    public void UndoRedo_Integration_ShouldWorkCorrectly()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 0);

        var op1 = CreateOp(nodeMember, "Property", 10, 0);
        var op2 = CreateOp(nodeMember, "Property", 20, 10);
        var op3 = CreateOp(nodeMember, "Property", 30, 20);

        op1.Apply();
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(10));

        op2.Apply();
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(20));

        op3.Apply();
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(30));

        op3.Revert();
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(20));

        op2.Revert();
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(10));

        op1.Revert();
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(0));
    }

    [Test]
    public void MergeAndApply_Integration_ShouldWorkCorrectly()
    {
        var nodeMember = new TestNodeMember<int>("TestProperty", 0);

        var op1 = CreateOp(nodeMember, "Property", 10, 0);
        var op2 = CreateOp(nodeMember, "Property", 20, 10);
        var op3 = CreateOp(nodeMember, "Property", 30, 20);

        op1.TryMerge(op2);
        op1.TryMerge(op3);

        op1.Apply();
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(30));

        op1.Revert();
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(0));
    }

    [Test]
    public void MixedOperations_Integration()
    {
        var nodeMember = new TestNodeMember<float>("TestProperty", 1.0f);
        var animation = new KeyFrameAnimation<float>();

        var valueOp = CreateOp(nodeMember, "Property", 2.0f, 1.0f);
        valueOp.Apply();
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(2.0f));

        var animOp = CreateOp(nodeMember, "Property.Animation", animation, null);
        animOp.Apply();
        Assert.That(nodeMember.GetProperty()!.Animation, Is.SameAs(animation));

        animOp.Revert();
        Assert.That(nodeMember.GetProperty()!.Animation, Is.Null);

        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(2.0f));

        valueOp.Revert();
        Assert.That(nodeMember.GetProperty()!.GetValue(), Is.EqualTo(1.0f));
    }

    #endregion

    #region Null Property Tests

    [Test]
    public void Apply_ShouldNotThrow_WhenPropertyIsNull()
    {
        var nodeMember = new DefaultNodeMember<int>();
        var op = CreateOp(nodeMember, "Property", 20, 10);

        Assert.DoesNotThrow(() => op.Apply());
    }

    [Test]
    public void Revert_ShouldNotThrow_WhenPropertyIsNull()
    {
        var nodeMember = new DefaultNodeMember<int>();
        var op = CreateOp(nodeMember, "Property", 20, 10);

        Assert.DoesNotThrow(() => op.Revert());
    }

    #endregion
}
