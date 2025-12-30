using Beutl.Animation.Easings;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Editor;

public class UpdateSplineEasingOperationTests
{
    private OperationSequenceGenerator _sequenceGenerator = null!;
    private TestCoreObject _parent = null!;

    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        _sequenceGenerator = new OperationSequenceGenerator();
        _parent = new TestCoreObject();
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

    #region Constructor Tests

    [Test]
    public void Constructor_ShouldSetEasing()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);

        // Act
        var op = CreateOp(easing, "X1", 0.5f, 0.25f);

        // Assert
        Assert.That(op.Easing, Is.SameAs(easing));
    }

    [Test]
    public void Constructor_ShouldSetPropertyPath()
    {
        // Arrange
        var easing = new SplineEasing();
        const string propertyPath = "X1";

        // Act
        var op = CreateOp(easing, propertyPath, 0.5f, 0.0f);

        // Assert
        Assert.That(op.PropertyPath, Is.EqualTo(propertyPath));
    }

    [Test]
    public void Constructor_ShouldSetNewValue()
    {
        // Arrange
        var easing = new SplineEasing();
        const float newValue = 0.75f;

        // Act
        var op = CreateOp(easing, "X1", newValue, 0.0f);

        // Assert
        Assert.That(op.NewValue, Is.EqualTo(newValue));
    }

    [Test]
    public void Constructor_ShouldSetOldValue()
    {
        // Arrange
        var easing = new SplineEasing();
        const float oldValue = 0.25f;

        // Act
        var op = CreateOp(easing, "X1", 0.5f, oldValue);

        // Assert
        Assert.That(op.OldValue, Is.EqualTo(oldValue));
    }

    [Test]
    public void Constructor_ShouldNotSetParentByDefault()
    {
        // Arrange
        var easing = new SplineEasing();

        // Act
        var op = CreateOp(easing, "X1", 0.5f, 0.0f);

        // Assert
        Assert.That(op.Parent, Is.Null);
    }

    [Test]
    public void Constructor_WithNestedPropertyPath_ShouldSetPropertyPath()
    {
        // Arrange
        var easing = new SplineEasing();
        const string propertyPath = "KeyFrame.Easing.X1";

        // Act
        var op = CreateOp(easing, propertyPath, 0.5f, 0.0f);

        // Assert
        Assert.That(op.PropertyPath, Is.EqualTo(propertyPath));
    }

    #endregion

    #region Property Setter Tests

    [Test]
    public void Easing_ShouldBeSettable()
    {
        // Arrange
        var easing1 = new SplineEasing(0.25f, 0.1f, 0.25f, 1.0f);
        var easing2 = new SplineEasing(0.5f, 0.5f, 0.5f, 0.5f);
        var op = CreateOp(easing1, "X1", 0.5f, 0.25f);

        // Act
        op.Easing = easing2;

        // Assert
        Assert.That(op.Easing, Is.SameAs(easing2));
    }

    [Test]
    public void PropertyPath_ShouldBeSettable()
    {
        // Arrange
        var easing = new SplineEasing();
        var op = CreateOp(easing, "X1", 0.5f, 0.0f);

        // Act
        op.PropertyPath = "Y1";

        // Assert
        Assert.That(op.PropertyPath, Is.EqualTo("Y1"));
    }

    [Test]
    public void NewValue_ShouldBeSettable()
    {
        // Arrange
        var easing = new SplineEasing();
        var op = CreateOp(easing, "X1", 0.5f, 0.0f);

        // Act
        op.NewValue = 0.75f;

        // Assert
        Assert.That(op.NewValue, Is.EqualTo(0.75f));
    }

    [Test]
    public void OldValue_ShouldBeSettable()
    {
        // Arrange
        var easing = new SplineEasing();
        var op = CreateOp(easing, "X1", 0.5f, 0.0f);

        // Act
        op.OldValue = 0.25f;

        // Assert
        Assert.That(op.OldValue, Is.EqualTo(0.25f));
    }

    [Test]
    public void Parent_ShouldBeSettable()
    {
        // Arrange
        var easing = new SplineEasing();
        var op = CreateOp(easing, "X1", 0.5f, 0.0f);

        // Act
        op.Parent = _parent;

        // Assert
        Assert.That(op.Parent, Is.SameAs(_parent));
    }

    [Test]
    public void Parent_CanBeSetToNull()
    {
        // Arrange
        var easing = new SplineEasing();
        var op = CreateOp(easing, "X1", 0.5f, 0.0f);
        op.Parent = _parent;

        // Act
        op.Parent = null;

        // Assert
        Assert.That(op.Parent, Is.Null);
    }

    #endregion

    #region Interface Implementation Tests

    [Test]
    public void ShouldImplementIPropertyPathProvider()
    {
        // Arrange
        var easing = new SplineEasing();
        var op = CreateOp(easing, "X1", 0.5f, 0.0f);

        // Assert
        Assert.That(op, Is.InstanceOf<IPropertyPathProvider>());
    }

    [Test]
    public void ShouldImplementIMergableChangeOperation()
    {
        // Arrange
        var easing = new SplineEasing();
        var op = CreateOp(easing, "X1", 0.5f, 0.0f);

        // Assert
        Assert.That(op, Is.InstanceOf<IMergableChangeOperation>());
    }

    [Test]
    public void IPropertyPathProvider_PropertyPath_ShouldReturnCorrectValue()
    {
        // Arrange
        var easing = new SplineEasing();
        var op = CreateOp(easing, "TestPath.X1", 0.5f, 0.0f);
        IPropertyPathProvider provider = op;

        // Assert
        Assert.That(provider.PropertyPath, Is.EqualTo("TestPath.X1"));
    }

    #endregion

    #region Apply Tests - X1 Property

    [Test]
    public void Apply_WithX1PropertyPath_ShouldSetNewValue()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "X1", 0.5f, 0.25f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(easing.X1, Is.EqualTo(0.5f));
    }

    [Test]
    public void Apply_WithNestedX1PropertyPath_ShouldSetNewValue()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "KeyFrame.Easing.X1", 0.6f, 0.25f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(easing.X1, Is.EqualTo(0.6f));
    }

    #endregion

    #region Apply Tests - Y1 Property

    [Test]
    public void Apply_WithY1PropertyPath_ShouldSetNewValue()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "Y1", 0.5f, 0.1f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(easing.Y1, Is.EqualTo(0.5f));
    }

    [Test]
    public void Apply_WithNestedY1PropertyPath_ShouldSetNewValue()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "Parent.Child.Y1", 0.7f, 0.1f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(easing.Y1, Is.EqualTo(0.7f));
    }

    #endregion

    #region Apply Tests - X2 Property

    [Test]
    public void Apply_WithX2PropertyPath_ShouldSetNewValue()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "X2", 0.5f, 0.75f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(easing.X2, Is.EqualTo(0.5f));
    }

    [Test]
    public void Apply_WithNestedX2PropertyPath_ShouldSetNewValue()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "Easing.X2", 0.8f, 0.75f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(easing.X2, Is.EqualTo(0.8f));
    }

    #endregion

    #region Apply Tests - Y2 Property

    [Test]
    public void Apply_WithY2PropertyPath_ShouldSetNewValue()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "Y2", 0.5f, 0.9f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(easing.Y2, Is.EqualTo(0.5f));
    }

    [Test]
    public void Apply_WithNestedY2PropertyPath_ShouldSetNewValue()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "Animation.KeyFrame.Easing.Y2", 1.0f, 0.9f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(easing.Y2, Is.EqualTo(1.0f));
    }

    #endregion

    #region Apply Tests - Error Handling

    [Test]
    public void Apply_WithUnknownPropertyPath_ShouldThrowArgumentException()
    {
        // Arrange
        var easing = new SplineEasing();
        var op = CreateOp(easing, "UnknownProperty", 0.5f, 0.0f);
        var context = new OperationExecutionContext(_parent);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => op.Apply(context));
        Assert.That(ex!.Message, Does.Contain("Unknown property name: UnknownProperty"));
    }

    [Test]
    public void Apply_WithNestedUnknownPropertyPath_ShouldThrowArgumentException()
    {
        // Arrange
        var easing = new SplineEasing();
        var op = CreateOp(easing, "Easing.Unknown", 0.5f, 0.0f);
        var context = new OperationExecutionContext(_parent);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => op.Apply(context));
        Assert.That(ex!.Message, Does.Contain("Unknown property name: Unknown"));
    }

    #endregion

    #region Revert Tests - X1 Property

    [Test]
    public void Revert_WithX1PropertyPath_ShouldSetOldValue()
    {
        // Arrange
        var easing = new SplineEasing(0.5f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "X1", 0.5f, 0.25f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Revert(context);

        // Assert
        Assert.That(easing.X1, Is.EqualTo(0.25f));
    }

    [Test]
    public void Revert_WithNestedX1PropertyPath_ShouldSetOldValue()
    {
        // Arrange
        var easing = new SplineEasing(0.6f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "KeyFrame.Easing.X1", 0.6f, 0.25f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Revert(context);

        // Assert
        Assert.That(easing.X1, Is.EqualTo(0.25f));
    }

    #endregion

    #region Revert Tests - Y1 Property

    [Test]
    public void Revert_WithY1PropertyPath_ShouldSetOldValue()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.5f, 0.75f, 0.9f);
        var op = CreateOp(easing, "Y1", 0.5f, 0.1f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Revert(context);

        // Assert
        Assert.That(easing.Y1, Is.EqualTo(0.1f));
    }

    #endregion

    #region Revert Tests - X2 Property

    [Test]
    public void Revert_WithX2PropertyPath_ShouldSetOldValue()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.5f, 0.9f);
        var op = CreateOp(easing, "X2", 0.5f, 0.75f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Revert(context);

        // Assert
        Assert.That(easing.X2, Is.EqualTo(0.75f));
    }

    #endregion

    #region Revert Tests - Y2 Property

    [Test]
    public void Revert_WithY2PropertyPath_ShouldSetOldValue()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.5f);
        var op = CreateOp(easing, "Y2", 0.5f, 0.9f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Revert(context);

        // Assert
        Assert.That(easing.Y2, Is.EqualTo(0.9f));
    }

    #endregion

    #region Revert Tests - Error Handling

    [Test]
    public void Revert_WithUnknownPropertyPath_ShouldThrowArgumentException()
    {
        // Arrange
        var easing = new SplineEasing();
        var op = CreateOp(easing, "UnknownProperty", 0.5f, 0.0f);
        var context = new OperationExecutionContext(_parent);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => op.Revert(context));
        Assert.That(ex!.Message, Does.Contain("Unknown property name: UnknownProperty"));
    }

    #endregion

    #region TryMerge Tests

    [Test]
    public void TryMerge_WithSameEasingAndPropertyPath_ShouldReturnTrueAndUpdateNewValue()
    {
        // Arrange
        var easing = new SplineEasing();
        var op1 = CreateOp(easing, "X1", 0.5f, 0.25f);
        var op2 = CreateOp(easing, "X1", 0.75f, 0.5f);

        // Act
        var result = op1.TryMerge(op2);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(op1.NewValue, Is.EqualTo(0.75f));
            Assert.That(op1.OldValue, Is.EqualTo(0.25f)); // OldValue should not change
        });
    }

    [Test]
    public void TryMerge_WithDifferentEasing_ShouldReturnFalse()
    {
        // Arrange
        var easing1 = new SplineEasing();
        var easing2 = new SplineEasing();
        var op1 = CreateOp(easing1, "X1", 0.5f, 0.25f);
        var op2 = CreateOp(easing2, "X1", 0.75f, 0.5f);

        // Act
        var result = op1.TryMerge(op2);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(op1.NewValue, Is.EqualTo(0.5f)); // NewValue should not change
        });
    }

    [Test]
    public void TryMerge_WithDifferentPropertyPath_ShouldReturnFalse()
    {
        // Arrange
        var easing = new SplineEasing();
        var op1 = CreateOp(easing, "X1", 0.5f, 0.25f);
        var op2 = CreateOp(easing, "Y1", 0.75f, 0.5f);

        // Act
        var result = op1.TryMerge(op2);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(op1.NewValue, Is.EqualTo(0.5f)); // NewValue should not change
        });
    }

    [Test]
    public void TryMerge_WithDifferentOperationType_ShouldReturnFalse()
    {
        // Arrange
        var easing = new SplineEasing();
        var op1 = CreateOp(easing, "X1", 0.5f, 0.25f);
        var op2 = new TestChangeOperation { SequenceNumber = _sequenceGenerator.GetNext() };

        // Act
        var result = op1.TryMerge(op2);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryMerge_WithSameNestedPropertyPath_ShouldReturnTrue()
    {
        // Arrange
        var easing = new SplineEasing();
        var op1 = CreateOp(easing, "KeyFrame.Easing.X1", 0.5f, 0.25f);
        var op2 = CreateOp(easing, "KeyFrame.Easing.X1", 0.75f, 0.5f);

        // Act
        var result = op1.TryMerge(op2);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void TryMerge_MultipleTimes_ShouldContinueToMerge()
    {
        // Arrange
        var easing = new SplineEasing();
        var op1 = CreateOp(easing, "X1", 0.3f, 0.25f);
        var op2 = CreateOp(easing, "X1", 0.5f, 0.3f);
        var op3 = CreateOp(easing, "X1", 0.75f, 0.5f);

        // Act
        var result1 = op1.TryMerge(op2);
        var result2 = op1.TryMerge(op3);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result1, Is.True);
            Assert.That(result2, Is.True);
            Assert.That(op1.NewValue, Is.EqualTo(0.75f));
            Assert.That(op1.OldValue, Is.EqualTo(0.25f));
        });
    }

    [Test]
    public void TryMerge_WithDifferentPropertyPath_ButSameEasing_ShouldReturnFalse()
    {
        // Arrange
        var easing = new SplineEasing();
        var op1 = CreateOp(easing, "X1", 0.5f, 0.25f);
        var op2 = CreateOp(easing, "X2", 0.75f, 0.5f);

        // Act
        var result = op1.TryMerge(op2);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryMerge_WithSamePath_ButDifferentEasing_ShouldReturnFalse()
    {
        // Arrange
        var easing1 = new SplineEasing(0.1f, 0.1f, 0.1f, 0.1f);
        var easing2 = new SplineEasing(0.2f, 0.2f, 0.2f, 0.2f);
        var op1 = CreateOp(easing1, "X1", 0.5f, 0.25f);
        var op2 = CreateOp(easing2, "X1", 0.75f, 0.5f);

        // Act
        var result = op1.TryMerge(op2);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region PropertyPath Parsing Tests

    [Test]
    public void PropertyPath_SimplePropertyName_ShouldWorkCorrectly()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "X1", 0.5f, 0.25f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(easing.X1, Is.EqualTo(0.5f));
    }

    [Test]
    public void PropertyPath_SingleDotPath_ShouldParseCorrectly()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "Easing.Y1", 0.5f, 0.1f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(easing.Y1, Is.EqualTo(0.5f));
    }

    [Test]
    public void PropertyPath_MultipleDotPath_ShouldParseCorrectly()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "KeyFrame.Animation.Easing.X2", 0.5f, 0.75f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(easing.X2, Is.EqualTo(0.5f));
    }

    [Test]
    public void PropertyPath_PathWithoutDot_ShouldUseWholeString()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "Y2", 0.5f, 0.9f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(easing.Y2, Is.EqualTo(0.5f));
    }

    #endregion

    #region SequenceNumber Tests

    [Test]
    public void SequenceNumber_ShouldBeSetFromSequenceGenerator()
    {
        // Arrange
        var easing = new SplineEasing();

        // Act
        var op = CreateOp(easing, "X1", 0.5f, 0.0f);

        // Assert
        Assert.That(op.SequenceNumber, Is.GreaterThan(0));
    }

    [Test]
    public void SequenceNumber_MultipleOperations_ShouldIncrement()
    {
        // Arrange
        var easing = new SplineEasing();

        // Act
        var op1 = CreateOp(easing, "X1", 0.5f, 0.0f);
        var op2 = CreateOp(easing, "Y1", 0.5f, 0.0f);

        // Assert
        Assert.That(op2.SequenceNumber, Is.GreaterThan(op1.SequenceNumber));
    }

    #endregion

    #region Apply and Revert Cycle Tests

    [Test]
    public void ApplyAndRevert_X1_ShouldRestoreOriginalValue()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "X1", 0.5f, 0.25f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);
        var valueAfterApply = easing.X1;
        op.Revert(context);
        var valueAfterRevert = easing.X1;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(valueAfterApply, Is.EqualTo(0.5f));
            Assert.That(valueAfterRevert, Is.EqualTo(0.25f));
        });
    }

    [Test]
    public void ApplyAndRevert_Y1_ShouldRestoreOriginalValue()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "Y1", 0.5f, 0.1f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);
        var valueAfterApply = easing.Y1;
        op.Revert(context);
        var valueAfterRevert = easing.Y1;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(valueAfterApply, Is.EqualTo(0.5f));
            Assert.That(valueAfterRevert, Is.EqualTo(0.1f));
        });
    }

    [Test]
    public void ApplyAndRevert_X2_ShouldRestoreOriginalValue()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "X2", 0.5f, 0.75f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);
        var valueAfterApply = easing.X2;
        op.Revert(context);
        var valueAfterRevert = easing.X2;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(valueAfterApply, Is.EqualTo(0.5f));
            Assert.That(valueAfterRevert, Is.EqualTo(0.75f));
        });
    }

    [Test]
    public void ApplyAndRevert_Y2_ShouldRestoreOriginalValue()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "Y2", 0.5f, 0.9f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);
        var valueAfterApply = easing.Y2;
        op.Revert(context);
        var valueAfterRevert = easing.Y2;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(valueAfterApply, Is.EqualTo(0.5f));
            Assert.That(valueAfterRevert, Is.EqualTo(0.9f));
        });
    }

    [Test]
    public void ApplyMultipleTimes_ShouldApplyNewValueEachTime()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "X1", 0.5f, 0.25f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);
        var valueAfterFirstApply = easing.X1;

        // Simulate something changed the value
        easing.X1 = 0.3f;

        op.Apply(context);
        var valueAfterSecondApply = easing.X1;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(valueAfterFirstApply, Is.EqualTo(0.5f));
            Assert.That(valueAfterSecondApply, Is.EqualTo(0.5f));
        });
    }

    [Test]
    public void RevertMultipleTimes_ShouldRevertToOldValueEachTime()
    {
        // Arrange
        var easing = new SplineEasing(0.5f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "X1", 0.5f, 0.25f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Revert(context);
        var valueAfterFirstRevert = easing.X1;

        // Simulate something changed the value
        easing.X1 = 0.6f;

        op.Revert(context);
        var valueAfterSecondRevert = easing.X1;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(valueAfterFirstRevert, Is.EqualTo(0.25f));
            Assert.That(valueAfterSecondRevert, Is.EqualTo(0.25f));
        });
    }

    #endregion

    #region Edge Cases

    [Test]
    public void Apply_WithZeroValues_ShouldWork()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "X1", 0.0f, 0.25f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(easing.X1, Is.EqualTo(0.0f));
    }

    [Test]
    public void Apply_WithNegativeValues_ShouldWork()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "Y1", -0.5f, 0.1f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(easing.Y1, Is.EqualTo(-0.5f));
    }

    [Test]
    public void Apply_WithValuesGreaterThanOne_ShouldWork()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "Y2", 1.5f, 0.9f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op.Apply(context);

        // Assert
        Assert.That(easing.Y2, Is.EqualTo(1.5f));
    }

    [Test]
    public void Apply_WithSameOldAndNewValue_ShouldStillApply()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "X1", 0.25f, 0.25f);
        var context = new OperationExecutionContext(_parent);

        // Act - should not throw
        Assert.DoesNotThrow(() => op.Apply(context));

        // Assert
        Assert.That(easing.X1, Is.EqualTo(0.25f));
    }

    [Test]
    public void TryMerge_AfterApply_ShouldStillWorkCorrectly()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op1 = CreateOp(easing, "X1", 0.5f, 0.25f);
        var op2 = CreateOp(easing, "X1", 0.75f, 0.5f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op1.Apply(context);
        var mergeResult = op1.TryMerge(op2);
        op1.Apply(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(mergeResult, Is.True);
            Assert.That(easing.X1, Is.EqualTo(0.75f));
            Assert.That(op1.OldValue, Is.EqualTo(0.25f));
        });
    }

    [Test]
    public void Revert_AfterMerge_ShouldRevertToOriginalOldValue()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op1 = CreateOp(easing, "X1", 0.5f, 0.25f);
        var op2 = CreateOp(easing, "X1", 0.75f, 0.5f);
        var context = new OperationExecutionContext(_parent);

        // Act
        op1.Apply(context);
        op1.TryMerge(op2);
        op1.Apply(context);
        op1.Revert(context);

        // Assert
        Assert.That(easing.X1, Is.EqualTo(0.25f));
    }

    #endregion

    #region Integration Tests

    [Test]
    public void Integration_FullUndoRedoCycle()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op = CreateOp(easing, "X1", 0.5f, 0.25f);
        var context = new OperationExecutionContext(_parent);

        // Act & Assert - Undo/Redo cycle
        Assert.That(easing.X1, Is.EqualTo(0.25f), "Initial value");

        op.Apply(context);
        Assert.That(easing.X1, Is.EqualTo(0.5f), "After first apply (redo)");

        op.Revert(context);
        Assert.That(easing.X1, Is.EqualTo(0.25f), "After first revert (undo)");

        op.Apply(context);
        Assert.That(easing.X1, Is.EqualTo(0.5f), "After second apply (redo)");

        op.Revert(context);
        Assert.That(easing.X1, Is.EqualTo(0.25f), "After second revert (undo)");
    }

    [Test]
    public void Integration_MultiplePropertiesInSequence()
    {
        // Arrange
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var opX1 = CreateOp(easing, "X1", 0.5f, 0.25f);
        var opY1 = CreateOp(easing, "Y1", 0.5f, 0.1f);
        var opX2 = CreateOp(easing, "X2", 0.5f, 0.75f);
        var opY2 = CreateOp(easing, "Y2", 0.5f, 0.9f);
        var context = new OperationExecutionContext(_parent);

        // Act
        opX1.Apply(context);
        opY1.Apply(context);
        opX2.Apply(context);
        opY2.Apply(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(easing.X1, Is.EqualTo(0.5f));
            Assert.That(easing.Y1, Is.EqualTo(0.5f));
            Assert.That(easing.X2, Is.EqualTo(0.5f));
            Assert.That(easing.Y2, Is.EqualTo(0.5f));
        });

        // Revert in reverse order
        opY2.Revert(context);
        opX2.Revert(context);
        opY1.Revert(context);
        opX1.Revert(context);

        // Assert original values restored
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
        // Arrange - Simulates dragging a control point through multiple positions
        var easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var op1 = CreateOp(easing, "X1", 0.3f, 0.25f);
        var op2 = CreateOp(easing, "X1", 0.4f, 0.3f);
        var op3 = CreateOp(easing, "X1", 0.5f, 0.4f);
        var context = new OperationExecutionContext(_parent);

        // Act - Apply first, then merge subsequent operations
        op1.Apply(context);
        Assert.That(easing.X1, Is.EqualTo(0.3f));

        op1.TryMerge(op2);
        op1.Apply(context);
        Assert.That(easing.X1, Is.EqualTo(0.4f));

        op1.TryMerge(op3);
        op1.Apply(context);
        Assert.That(easing.X1, Is.EqualTo(0.5f));

        // Undo should go back to original value
        op1.Revert(context);
        Assert.That(easing.X1, Is.EqualTo(0.25f));

        // Redo should go to final merged value
        op1.Apply(context);
        Assert.That(easing.X1, Is.EqualTo(0.5f));
    }

    #endregion

    #region Test Helper Classes

    private class TestCoreObject : CoreObject
    {
    }

    private class TestChangeOperation : ChangeOperation
    {
        public override void Apply(OperationExecutionContext context)
        {
        }

        public override void Revert(OperationExecutionContext context)
        {
        }
    }

    #endregion
}
