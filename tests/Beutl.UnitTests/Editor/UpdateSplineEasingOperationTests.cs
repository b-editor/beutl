using Beutl.Animation.Easings;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Editor;

public class UpdateSplineEasingOperationTests
{
    private OperationSequenceGenerator _sequenceGenerator = null!;

    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        _sequenceGenerator = new OperationSequenceGenerator();
    }

    private UpdateSplineEasingOperation CreateOp(
        SplineEasing easing,
        string propertyPath,
        float newValue,
        float oldValue)
    {
        return new UpdateSplineEasingOperation(easing, propertyPath, newValue, oldValue)
        {
            SequenceNumber = _sequenceGenerator.GetNext()
        };
    }

    private static float GetControlPoint(SplineEasing easing, string name) => name switch
    {
        "X1" => easing.X1,
        "Y1" => easing.Y1,
        "X2" => easing.X2,
        _ => easing.Y2,
    };

    #region Constructor Tests

    [Test]
    public void Constructor_ShouldSetEasingPathAndValues()
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);

        var op = CreateOp(easing, "X1", 0.5f, 0.25f);

        Assert.Multiple(() =>
        {
            Assert.That(op.Easing, Is.SameAs(easing));
            Assert.That(op.PropertyPath, Is.EqualTo("X1"));
            Assert.That(op.NewValue, Is.EqualTo(0.5f));
            Assert.That(op.OldValue, Is.EqualTo(0.25f));
        });
    }

    #endregion

    #region Apply and Revert Tests

    [TestCase("X1", 0.5f, 0.25f)]
    [TestCase("Y1", 0.5f, 0.1f)]
    [TestCase("X2", 0.5f, 0.75f)]
    [TestCase("Y2", 0.5f, 0.9f)]
    public void ApplyAndRevert_ShouldRestoreOriginalValue(string propertyName, float newValue, float oldValue)
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, propertyName, newValue, oldValue);

        op.Apply();
        var valueAfterApply = GetControlPoint(easing, propertyName);
        op.Revert();
        var valueAfterRevert = GetControlPoint(easing, propertyName);

        Assert.Multiple(() =>
        {
            Assert.That(valueAfterApply, Is.EqualTo(newValue));
            Assert.That(valueAfterRevert, Is.EqualTo(oldValue));
        });
    }

    // Only the last segment of the path names the control point.
    [TestCase("KeyFrame.Easing.X1", "X1", 0.6f, 0.25f)]
    [TestCase("Easing.Y1", "Y1", 0.5f, 0.1f)]
    [TestCase("Parent.Child.Y1", "Y1", 0.7f, 0.1f)]
    [TestCase("Easing.X2", "X2", 0.8f, 0.75f)]
    [TestCase("KeyFrame.Animation.Easing.X2", "X2", 0.5f, 0.75f)]
    [TestCase("Animation.KeyFrame.Easing.Y2", "Y2", 1.0f, 0.9f)]
    public void ApplyAndRevert_WithNestedPropertyPath_ShouldUseTheLastSegment(
        string propertyPath, string propertyName, float newValue, float oldValue)
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, propertyPath, newValue, oldValue);

        op.Apply();
        var valueAfterApply = GetControlPoint(easing, propertyName);
        op.Revert();
        var valueAfterRevert = GetControlPoint(easing, propertyName);

        Assert.Multiple(() =>
        {
            Assert.That(valueAfterApply, Is.EqualTo(newValue));
            Assert.That(valueAfterRevert, Is.EqualTo(oldValue));
        });
    }

    [TestCase("UnknownProperty", "UnknownProperty", false)]
    [TestCase("Easing.Unknown", "Unknown", false)]
    [TestCase("UnknownProperty", "UnknownProperty", true)]
    public void UnknownPropertyPath_ShouldThrowArgumentException(string propertyPath, string propertyName, bool revert)
    {
        var easing = new SplineEasing();
        var op = CreateOp(easing, propertyPath, 0.5f, 0.0f);

        var ex = Assert.Throws<ArgumentException>(() =>
        {
            if (revert)
                op.Revert();
            else
                op.Apply();
        });
        Assert.That(ex!.Message, Does.Contain($"Unknown property name: {propertyName}"));
    }

    [Test]
    public void ApplyMultipleTimes_ShouldApplyNewValueEachTime()
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "X1", 0.5f, 0.25f);

        op.Apply();
        var valueAfterFirstApply = easing.X1;

        // Simulate something changed the value
        easing.X1 = 0.3f;

        op.Apply();
        var valueAfterSecondApply = easing.X1;

        Assert.Multiple(() =>
        {
            Assert.That(valueAfterFirstApply, Is.EqualTo(0.5f));
            Assert.That(valueAfterSecondApply, Is.EqualTo(0.5f));
        });
    }

    [Test]
    public void RevertMultipleTimes_ShouldRevertToOldValueEachTime()
    {
        var easing = new SplineEasing(0.5f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "X1", 0.5f, 0.25f);

        op.Revert();
        var valueAfterFirstRevert = easing.X1;

        // Simulate something changed the value
        easing.X1 = 0.6f;

        op.Revert();
        var valueAfterSecondRevert = easing.X1;

        Assert.Multiple(() =>
        {
            Assert.That(valueAfterFirstRevert, Is.EqualTo(0.25f));
            Assert.That(valueAfterSecondRevert, Is.EqualTo(0.25f));
        });
    }

    #endregion

    #region TryMerge Tests

    [Test]
    public void TryMerge_WithSameEasingAndPropertyPath_ShouldReturnTrueAndUpdateNewValue()
    {
        var easing = new SplineEasing();
        var op1 = CreateOp(easing, "X1", 0.5f, 0.25f);
        var op2 = CreateOp(easing, "X1", 0.75f, 0.5f);

        var result = op1.TryMerge(op2);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(op1.NewValue, Is.EqualTo(0.75f));
            Assert.That(op1.OldValue, Is.EqualTo(0.25f));
        });
    }

    [Test]
    public void TryMerge_WithDifferentEasing_ShouldReturnFalse()
    {
        var easing1 = new SplineEasing();
        var easing2 = new SplineEasing();
        var op1 = CreateOp(easing1, "X1", 0.5f, 0.25f);
        var op2 = CreateOp(easing2, "X1", 0.75f, 0.5f);

        var result = op1.TryMerge(op2);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(op1.NewValue, Is.EqualTo(0.5f));
        });
    }

    [Test]
    public void TryMerge_WithDifferentPropertyPath_ShouldReturnFalse()
    {
        var easing = new SplineEasing();
        var op1 = CreateOp(easing, "X1", 0.5f, 0.25f);
        var op2 = CreateOp(easing, "Y1", 0.75f, 0.5f);

        var result = op1.TryMerge(op2);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(op1.NewValue, Is.EqualTo(0.5f));
        });
    }

    [Test]
    public void TryMerge_WithDifferentOperationType_ShouldReturnFalse()
    {
        var easing = new SplineEasing();
        var op1 = CreateOp(easing, "X1", 0.5f, 0.25f);
        var op2 = new TestChangeOperation { SequenceNumber = _sequenceGenerator.GetNext() };

        var result = op1.TryMerge(op2);

        Assert.That(result, Is.False);
    }

    #endregion

    #region IsNoop Tests

    [Test]
    public void IsNoop_IsTrue_WhenMergedWritesReturnToTheOldValue()
    {
        var easing = new SplineEasing();
        var op = CreateOp(easing, "X1", 0.5f, 0.25f);

        op.TryMerge(CreateOp(easing, "X1", 0.25f, 0.5f));

        Assert.That(op.IsNoop, Is.True);
    }

    [Test]
    public void IsNoop_TreatsAnUntouchedNaNControlPointAsUnchanged()
    {
        var easing = new SplineEasing();
        var op = CreateOp(easing, "Y1", float.NaN, float.NaN);

        Assert.That(op.IsNoop, Is.True);
    }

    [Test]
    public void IsNoop_IsFalse_WhileTheValueDiffers()
    {
        var easing = new SplineEasing();
        var op = CreateOp(easing, "Y1", 0.5f, 0.25f);

        Assert.That(op.IsNoop, Is.False);
    }

    #endregion

    #region Integration Tests

    [Test]
    public void Integration_MultiplePropertiesInSequence()
    {
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var opX1 = CreateOp(easing, "X1", 0.5f, 0.25f);
        var opY1 = CreateOp(easing, "Y1", 0.5f, 0.1f);
        var opX2 = CreateOp(easing, "X2", 0.5f, 0.75f);
        var opY2 = CreateOp(easing, "Y2", 0.5f, 0.9f);

        opX1.Apply();
        opY1.Apply();
        opX2.Apply();
        opY2.Apply();

        Assert.Multiple(() =>
        {
            Assert.That(easing.X1, Is.EqualTo(0.5f));
            Assert.That(easing.Y1, Is.EqualTo(0.5f));
            Assert.That(easing.X2, Is.EqualTo(0.5f));
            Assert.That(easing.Y2, Is.EqualTo(0.5f));
        });

        opY2.Revert();
        opX2.Revert();
        opY1.Revert();
        opX1.Revert();

        Assert.Multiple(() =>
        {
            Assert.That(easing.X1, Is.EqualTo(0.25f));
            Assert.That(easing.Y1, Is.EqualTo(0.1f));
            Assert.That(easing.X2, Is.EqualTo(0.75f));
            Assert.That(easing.Y2, Is.EqualTo(0.9f));
        });
    }

    [Test]
    public void Integration_MergeAndUndoRedo()
    {
        // Simulates dragging a control point through multiple positions
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op1 = CreateOp(easing, "X1", 0.3f, 0.25f);
        var op2 = CreateOp(easing, "X1", 0.4f, 0.3f);
        var op3 = CreateOp(easing, "X1", 0.5f, 0.4f);

        // Apply first, then merge subsequent operations
        op1.Apply();
        Assert.That(easing.X1, Is.EqualTo(0.3f));

        op1.TryMerge(op2);
        op1.Apply();
        Assert.That(easing.X1, Is.EqualTo(0.4f));

        op1.TryMerge(op3);
        op1.Apply();
        Assert.That(easing.X1, Is.EqualTo(0.5f));

        op1.Revert();
        Assert.That(easing.X1, Is.EqualTo(0.25f));

        op1.Apply();
        Assert.That(easing.X1, Is.EqualTo(0.5f));
    }

    #endregion

    #region Test Helper Classes

    private class TestChangeOperation : ChangeOperation
    {
        public override void Apply()
        {
        }

        public override void Revert()
        {
        }
    }

    #endregion
}
