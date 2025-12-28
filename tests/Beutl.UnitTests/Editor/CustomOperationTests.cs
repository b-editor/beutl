using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Editor;

public class CustomOperationTests
{
    private TestCoreObject _root = null!;
    private OperationExecutionContext _context = null!;
    private OperationSequenceGenerator _sequenceGenerator = null!;

    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        _root = new TestCoreObject();
        _context = new OperationExecutionContext(_root);
        _sequenceGenerator = new OperationSequenceGenerator();
    }

    #region Constructor Tests

    [Test]
    public void Constructor_ShouldCreateInstance_WithValidArguments()
    {
        // Arrange
        Action<OperationExecutionContext> apply = _ => { };
        Action<OperationExecutionContext> revert = _ => { };

        // Act
        var operation = new CustomOperation(apply, revert, "Test Description")
        {
            SequenceNumber = 1
        };

        // Assert
        Assert.That(operation, Is.Not.Null);
        Assert.That(operation.Description, Is.EqualTo("Test Description"));
        Assert.That(operation.SequenceNumber, Is.EqualTo(1));
    }

    [Test]
    public void Constructor_ShouldThrowArgumentNullException_WhenApplyIsNull()
    {
        // Arrange
        Action<OperationExecutionContext> revert = _ => { };

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CustomOperation(null!, revert, "Test") { SequenceNumber = 1 });

        Assert.That(exception!.ParamName, Is.EqualTo("apply"));
    }

    [Test]
    public void Constructor_ShouldThrowArgumentNullException_WhenRevertIsNull()
    {
        // Arrange
        Action<OperationExecutionContext> apply = _ => { };

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CustomOperation(apply, null!, "Test") { SequenceNumber = 1 });

        Assert.That(exception!.ParamName, Is.EqualTo("revert"));
    }

    [Test]
    public void Constructor_ShouldAllowNullDescription()
    {
        // Arrange
        Action<OperationExecutionContext> apply = _ => { };
        Action<OperationExecutionContext> revert = _ => { };

        // Act
        var operation = new CustomOperation(apply, revert, null)
        {
            SequenceNumber = 1
        };

        // Assert
        Assert.That(operation.Description, Is.Null);
    }

    #endregion

    #region Apply Tests

    [Test]
    public void Apply_ShouldInvokeApplyAction()
    {
        // Arrange
        bool applyInvoked = false;
        var operation = new CustomOperation(
            _ => applyInvoked = true,
            _ => { },
            "Test")
        {
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(applyInvoked, Is.True);
    }

    [Test]
    public void Apply_ShouldPassContextToAction()
    {
        // Arrange
        OperationExecutionContext? receivedContext = null;
        var operation = new CustomOperation(
            ctx => receivedContext = ctx,
            _ => { },
            "Test")
        {
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(receivedContext, Is.SameAs(_context));
    }

    [Test]
    public void Apply_ShouldModifyState()
    {
        // Arrange
        _root.Value = 0;
        var operation = new CustomOperation(
            _ => _root.Value = 100,
            _ => _root.Value = 0,
            "Set Value")
        {
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(_root.Value, Is.EqualTo(100));
    }

    #endregion

    #region Revert Tests

    [Test]
    public void Revert_ShouldInvokeRevertAction()
    {
        // Arrange
        bool revertInvoked = false;
        var operation = new CustomOperation(
            _ => { },
            _ => revertInvoked = true,
            "Test")
        {
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(revertInvoked, Is.True);
    }

    [Test]
    public void Revert_ShouldPassContextToAction()
    {
        // Arrange
        OperationExecutionContext? receivedContext = null;
        var operation = new CustomOperation(
            _ => { },
            ctx => receivedContext = ctx,
            "Test")
        {
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(receivedContext, Is.SameAs(_context));
    }

    [Test]
    public void Revert_ShouldRestoreState()
    {
        // Arrange
        _root.Value = 100;
        var operation = new CustomOperation(
            _ => _root.Value = 100,
            _ => _root.Value = 0,
            "Set Value")
        {
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(_root.Value, Is.EqualTo(0));
    }

    [Test]
    public void ApplyThenRevert_ShouldRestoreOriginalState()
    {
        // Arrange
        _root.Value = 50;
        var operation = new CustomOperation(
            _ => _root.Value = 200,
            _ => _root.Value = 50,
            "Modify Value")
        {
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);
        Assert.That(_root.Value, Is.EqualTo(200));
        operation.Revert(_context);

        // Assert
        Assert.That(_root.Value, Is.EqualTo(50));
    }

    #endregion

    #region Create Static Method Tests

    [Test]
    public void Create_ShouldReturnCustomOperation_WithValidArguments()
    {
        // Arrange & Act
        var operation = CustomOperation.Create(
            () => { },
            () => { },
            _sequenceGenerator,
            "Test Operation");

        // Assert
        Assert.That(operation, Is.Not.Null);
        Assert.That(operation, Is.InstanceOf<CustomOperation>());
    }

    [Test]
    public void Create_ShouldSetSequenceNumber()
    {
        // Arrange
        var generator = new OperationSequenceGenerator();

        // Act
        var operation1 = CustomOperation.Create(() => { }, () => { }, generator);
        var operation2 = CustomOperation.Create(() => { }, () => { }, generator);

        // Assert
        Assert.That(operation1.SequenceNumber, Is.EqualTo(1));
        Assert.That(operation2.SequenceNumber, Is.EqualTo(2));
    }

    [Test]
    public void Create_ShouldSetDescription()
    {
        // Arrange & Act
        var operation = CustomOperation.Create(
            () => { },
            () => { },
            _sequenceGenerator,
            "My Description");

        // Assert
        Assert.That(operation.Description, Is.EqualTo("My Description"));
    }

    [Test]
    public void Create_ShouldAllowNullDescription()
    {
        // Arrange & Act
        var operation = CustomOperation.Create(
            () => { },
            () => { },
            _sequenceGenerator,
            null);

        // Assert
        Assert.That(operation.Description, Is.Null);
    }

    [Test]
    public void Create_ShouldThrowArgumentNullException_WhenDoActionIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            CustomOperation.Create(null!, () => { }, _sequenceGenerator));
    }

    [Test]
    public void Create_ShouldThrowArgumentNullException_WhenUndoActionIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            CustomOperation.Create(() => { }, null!, _sequenceGenerator));
    }

    [Test]
    public void Create_ShouldThrowArgumentNullException_WhenSequenceGeneratorIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            CustomOperation.Create(() => { }, () => { }, null!));
    }

    [Test]
    public void Create_Apply_ShouldInvokeDoAction()
    {
        // Arrange
        bool doActionInvoked = false;
        var operation = CustomOperation.Create(
            () => doActionInvoked = true,
            () => { },
            _sequenceGenerator);

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(doActionInvoked, Is.True);
    }

    [Test]
    public void Create_Revert_ShouldInvokeUndoAction()
    {
        // Arrange
        bool undoActionInvoked = false;
        var operation = CustomOperation.Create(
            () => { },
            () => undoActionInvoked = true,
            _sequenceGenerator);

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(undoActionInvoked, Is.True);
    }

    [Test]
    public void Create_ShouldSupportStateModification()
    {
        // Arrange
        _root.Value = 0;
        var operation = CustomOperation.Create(
            () => _root.Value = 999,
            () => _root.Value = 0,
            _sequenceGenerator,
            "Set to 999");

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(_root.Value, Is.EqualTo(999));

        // Revert
        operation.Revert(_context);
        Assert.That(_root.Value, Is.EqualTo(0));
    }

    #endregion

    #region CaptureState Static Method Tests

    [Test]
    public void CaptureState_ShouldReturnBuilder_WithValidArguments()
    {
        // Arrange & Act
        var builder = CustomOperation.CaptureState(
            () => _root.Value,
            value => _root.Value = value,
            _sequenceGenerator,
            "State Capture");

        // Assert
        Assert.That(builder, Is.Not.Null);
        Assert.That(builder, Is.InstanceOf<StateCapturingOperationBuilder<int>>());
    }

    [Test]
    public void CaptureState_ShouldThrowArgumentNullException_WhenCaptureStateIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            CustomOperation.CaptureState<int>(null!, _ => { }, _sequenceGenerator));
    }

    [Test]
    public void CaptureState_ShouldThrowArgumentNullException_WhenApplyStateIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            CustomOperation.CaptureState(() => 0, null!, _sequenceGenerator));
    }

    [Test]
    public void CaptureState_ShouldThrowArgumentNullException_WhenSequenceGeneratorIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            CustomOperation.CaptureState(() => 0, _ => { }, null!));
    }

    [Test]
    public void CaptureState_ShouldCaptureInitialState()
    {
        // Arrange
        _root.Value = 42;

        // Act
        var builder = CustomOperation.CaptureState(
            () => _root.Value,
            value => _root.Value = value,
            _sequenceGenerator);

        // Modify state after capture
        _root.Value = 100;

        var operation = builder.Complete();

        // Apply should restore to after state (100)
        _root.Value = 0;
        operation.Apply(_context);
        Assert.That(_root.Value, Is.EqualTo(100));

        // Revert should restore to before state (42)
        operation.Revert(_context);
        Assert.That(_root.Value, Is.EqualTo(42));
    }

    #endregion

    #region StateCapturingOperationBuilder Tests

    [Test]
    public void StateCapturingOperationBuilder_Complete_ShouldReturnCustomOperation()
    {
        // Arrange
        _root.Value = 10;
        var builder = CustomOperation.CaptureState(
            () => _root.Value,
            value => _root.Value = value,
            _sequenceGenerator,
            "Value Change");

        // Act
        _root.Value = 20;
        var operation = builder.Complete();

        // Assert
        Assert.That(operation, Is.Not.Null);
        Assert.That(operation, Is.InstanceOf<CustomOperation>());
        Assert.That(operation.Description, Is.EqualTo("Value Change"));
    }

    [Test]
    public void StateCapturingOperationBuilder_Complete_ShouldSetSequenceNumber()
    {
        // Arrange
        var generator = new OperationSequenceGenerator();
        var builder = CustomOperation.CaptureState(
            () => _root.Value,
            value => _root.Value = value,
            generator);

        // Act
        var operation = builder.Complete();

        // Assert
        Assert.That(operation.SequenceNumber, Is.EqualTo(1));
    }

    [Test]
    public void StateCapturingOperationBuilder_Complete_Apply_ShouldRestoreAfterState()
    {
        // Arrange
        _root.Value = 5;
        var builder = CustomOperation.CaptureState(
            () => _root.Value,
            value => _root.Value = value,
            _sequenceGenerator);

        _root.Value = 15;
        var operation = builder.Complete();

        // Set to different value
        _root.Value = 999;

        // Act
        operation.Apply(_context);

        // Assert - should be after state (15)
        Assert.That(_root.Value, Is.EqualTo(15));
    }

    [Test]
    public void StateCapturingOperationBuilder_Complete_Revert_ShouldRestoreBeforeState()
    {
        // Arrange
        _root.Value = 25;
        var builder = CustomOperation.CaptureState(
            () => _root.Value,
            value => _root.Value = value,
            _sequenceGenerator);

        _root.Value = 75;
        var operation = builder.Complete();

        // Set to different value
        _root.Value = 999;

        // Act
        operation.Revert(_context);

        // Assert - should be before state (25)
        Assert.That(_root.Value, Is.EqualTo(25));
    }

    [Test]
    public void StateCapturingOperationBuilder_ShouldWorkWithComplexTypes()
    {
        // Arrange
        _root.Name = "Original";
        var builder = CustomOperation.CaptureState(
            () => _root.Name,
            value => _root.Name = value,
            _sequenceGenerator,
            "Name Change");

        _root.Name = "Modified";
        var operation = builder.Complete();

        // Act & Assert
        _root.Name = "Something Else";

        operation.Apply(_context);
        Assert.That(_root.Name, Is.EqualTo("Modified"));

        operation.Revert(_context);
        Assert.That(_root.Name, Is.EqualTo("Original"));
    }

    [Test]
    public void StateCapturingOperationBuilder_ShouldWorkWithNullValues()
    {
        // Arrange
        _root.Name = null;
        var builder = CustomOperation.CaptureState(
            () => _root.Name,
            value => _root.Name = value,
            _sequenceGenerator);

        _root.Name = "Not Null";
        var operation = builder.Complete();

        // Act & Assert
        operation.Revert(_context);
        Assert.That(_root.Name, Is.Null);

        operation.Apply(_context);
        Assert.That(_root.Name, Is.EqualTo("Not Null"));
    }

    [Test]
    public void StateCapturingOperationBuilder_Complete_ShouldWorkWithNoStateChange()
    {
        // Arrange
        _root.Value = 100;
        var builder = CustomOperation.CaptureState(
            () => _root.Value,
            value => _root.Value = value,
            _sequenceGenerator);

        // No state change - complete immediately
        var operation = builder.Complete();

        // Act & Assert
        _root.Value = 999;

        operation.Apply(_context);
        Assert.That(_root.Value, Is.EqualTo(100)); // after state = before state

        operation.Revert(_context);
        Assert.That(_root.Value, Is.EqualTo(100)); // before state = after state
    }

    #endregion

    #region Integration Tests

    [Test]
    public void CustomOperation_ShouldWorkWithContextFindObject()
    {
        // Arrange - Use root object since it's always findable by context
        _root.Value = 0;
        var rootId = _root.Id;

        var operation = new CustomOperation(
            ctx =>
            {
                var obj = ctx.FindObject(rootId) as TestCoreObject;
                if (obj != null) obj.Value = 500;
            },
            ctx =>
            {
                var obj = ctx.FindObject(rootId) as TestCoreObject;
                if (obj != null) obj.Value = 0;
            },
            "Modify Root via FindObject")
        {
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(_root.Value, Is.EqualTo(500));

        operation.Revert(_context);
        Assert.That(_root.Value, Is.EqualTo(0));
    }

    [Test]
    public void MultipleOperations_ShouldMaintainIndependentState()
    {
        // Arrange
        _root.Value = 0;
        _root.Name = "Initial";

        var valueOperation = CustomOperation.Create(
            () => _root.Value = 100,
            () => _root.Value = 0,
            _sequenceGenerator,
            "Value Op");

        var nameOperation = CustomOperation.Create(
            () => _root.Name = "Changed",
            () => _root.Name = "Initial",
            _sequenceGenerator,
            "Name Op");

        // Act & Assert
        valueOperation.Apply(_context);
        Assert.That(_root.Value, Is.EqualTo(100));
        Assert.That(_root.Name, Is.EqualTo("Initial"));

        nameOperation.Apply(_context);
        Assert.That(_root.Value, Is.EqualTo(100));
        Assert.That(_root.Name, Is.EqualTo("Changed"));

        valueOperation.Revert(_context);
        Assert.That(_root.Value, Is.EqualTo(0));
        Assert.That(_root.Name, Is.EqualTo("Changed"));

        nameOperation.Revert(_context);
        Assert.That(_root.Value, Is.EqualTo(0));
        Assert.That(_root.Name, Is.EqualTo("Initial"));
    }

    [Test]
    public void SequenceNumber_ShouldBeUniqueAcrossOperations()
    {
        // Arrange
        var generator = new OperationSequenceGenerator();
        var operations = new List<CustomOperation>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            operations.Add(CustomOperation.Create(
                () => { },
                () => { },
                generator,
                $"Op {i}"));
        }

        // Assert
        var sequenceNumbers = operations.Select(op => op.SequenceNumber).ToList();
        Assert.That(sequenceNumbers, Is.Unique);
        Assert.That(sequenceNumbers, Is.EqualTo(Enumerable.Range(1, 10).Select(i => (long)i).ToList()));
    }

    #endregion

    private class TestCoreObject : CoreObject
    {
        public int Value { get; set; }
        public new string? Name { get; set; }
    }
}
