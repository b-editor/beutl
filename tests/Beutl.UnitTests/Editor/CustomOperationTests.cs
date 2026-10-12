using Beutl.Editor;
using Beutl.Editor.Operations;

namespace Beutl.UnitTests.Editor;

public class CustomOperationTests
{
    private TestCoreObject _root = null!;
    private OperationExecutionContext _context = null!;
    private OperationSequenceGenerator _sequenceGenerator = null!;

    [SetUp]
    public void Setup()
    {
        _root = new TestCoreObject();
        _context = new OperationExecutionContext(_root);
        _sequenceGenerator = new OperationSequenceGenerator();
    }

    #region Constructor Tests

    [Test]
    public void Constructor_ShouldCreateInstance_WithValidArguments()
    {
        Action<OperationExecutionContext> apply = _ => { };
        Action<OperationExecutionContext> revert = _ => { };

        var operation = new CustomOperation(apply, revert, "Test Description")
        {
            SequenceNumber = 1
        };

        Assert.That(operation, Is.Not.Null);
        Assert.That(operation.Description, Is.EqualTo("Test Description"));
        Assert.That(operation.SequenceNumber, Is.EqualTo(1));
    }

    [Test]
    public void Constructor_ShouldThrowArgumentNullException_WhenApplyIsNull()
    {
        Action<OperationExecutionContext> revert = _ => { };

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CustomOperation(null!, revert, "Test") { SequenceNumber = 1 });

        Assert.That(exception!.ParamName, Is.EqualTo("apply"));
    }

    [Test]
    public void Constructor_ShouldThrowArgumentNullException_WhenRevertIsNull()
    {
        Action<OperationExecutionContext> apply = _ => { };

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CustomOperation(apply, null!, "Test") { SequenceNumber = 1 });

        Assert.That(exception!.ParamName, Is.EqualTo("revert"));
    }

    [Test]
    public void Constructor_ShouldAllowNullDescription()
    {
        Action<OperationExecutionContext> apply = _ => { };
        Action<OperationExecutionContext> revert = _ => { };

        var operation = new CustomOperation(apply, revert, null)
        {
            SequenceNumber = 1
        };

        Assert.That(operation.Description, Is.Null);
    }

    #endregion

    #region Apply Tests

    [Test]
    public void Apply_ShouldPassContextToAction()
    {
        OperationExecutionContext? receivedContext = null;
        var operation = new CustomOperation(
            ctx => receivedContext = ctx,
            _ => { },
            "Test")
        {
            SequenceNumber = 1
        };

        operation.Apply(_context);

        Assert.That(receivedContext, Is.SameAs(_context));
    }

    #endregion

    #region Revert Tests

    [Test]
    public void Revert_ShouldPassContextToAction()
    {
        OperationExecutionContext? receivedContext = null;
        var operation = new CustomOperation(
            _ => { },
            ctx => receivedContext = ctx,
            "Test")
        {
            SequenceNumber = 1
        };

        operation.Revert(_context);

        Assert.That(receivedContext, Is.SameAs(_context));
    }

    [Test]
    public void ApplyThenRevert_ShouldRestoreOriginalState()
    {
        _root.Value = 50;
        var operation = new CustomOperation(
            _ => _root.Value = 200,
            _ => _root.Value = 50,
            "Modify Value")
        {
            SequenceNumber = 1
        };

        operation.Apply(_context);
        Assert.That(_root.Value, Is.EqualTo(200));
        operation.Revert(_context);

        Assert.That(_root.Value, Is.EqualTo(50));
    }

    #endregion

    #region Create Static Method Tests

    [Test]
    public void Create_ShouldSetDescription()
    {
        var operation = CustomOperation.Create(
            () => { },
            () => { },
            _sequenceGenerator,
            "My Description");

        Assert.That(operation.Description, Is.EqualTo("My Description"));
    }

    [Test]
    public void Create_ShouldAllowNullDescription()
    {
        var operation = CustomOperation.Create(
            () => { },
            () => { },
            _sequenceGenerator,
            null);

        Assert.That(operation.Description, Is.Null);
    }

    [Test]
    public void Create_ShouldThrowArgumentNullException_WhenDoActionIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CustomOperation.Create(null!, () => { }, _sequenceGenerator));
    }

    [Test]
    public void Create_ShouldThrowArgumentNullException_WhenUndoActionIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CustomOperation.Create(() => { }, null!, _sequenceGenerator));
    }

    [Test]
    public void Create_ShouldThrowArgumentNullException_WhenSequenceGeneratorIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CustomOperation.Create(() => { }, () => { }, null!));
    }

    [Test]
    public void Create_ShouldSupportStateModification()
    {
        _root.Value = 0;
        var operation = CustomOperation.Create(
            () => _root.Value = 999,
            () => _root.Value = 0,
            _sequenceGenerator,
            "Set to 999");

        operation.Apply(_context);

        Assert.That(_root.Value, Is.EqualTo(999));

        operation.Revert(_context);
        Assert.That(_root.Value, Is.EqualTo(0));
    }

    #endregion

    #region CaptureState Static Method Tests

    [Test]
    public void CaptureState_ShouldReturnBuilder_WithValidArguments()
    {
        var builder = CustomOperation.CaptureState(
            () => _root.Value,
            value => _root.Value = value,
            _sequenceGenerator,
            "State Capture");

        Assert.That(builder, Is.Not.Null);
        Assert.That(builder, Is.InstanceOf<StateCapturingOperationBuilder<int>>());
    }

    [Test]
    public void CaptureState_ShouldThrowArgumentNullException_WhenCaptureStateIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CustomOperation.CaptureState<int>(null!, _ => { }, _sequenceGenerator));
    }

    [Test]
    public void CaptureState_ShouldThrowArgumentNullException_WhenApplyStateIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CustomOperation.CaptureState(() => 0, null!, _sequenceGenerator));
    }

    [Test]
    public void CaptureState_ShouldThrowArgumentNullException_WhenSequenceGeneratorIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CustomOperation.CaptureState(() => 0, _ => { }, null!));
    }

    [Test]
    public void CaptureState_ShouldCaptureInitialState()
    {
        _root.Value = 42;

        var builder = CustomOperation.CaptureState(
            () => _root.Value,
            value => _root.Value = value,
            _sequenceGenerator);

        _root.Value = 100;

        var operation = builder.Complete();

        _root.Value = 0;
        operation.Apply(_context);
        Assert.That(_root.Value, Is.EqualTo(100));

        operation.Revert(_context);
        Assert.That(_root.Value, Is.EqualTo(42));
    }

    #endregion

    #region StateCapturingOperationBuilder Tests

    [Test]
    public void StateCapturingOperationBuilder_Complete_ShouldReturnCustomOperation()
    {
        _root.Value = 10;
        var builder = CustomOperation.CaptureState(
            () => _root.Value,
            value => _root.Value = value,
            _sequenceGenerator,
            "Value Change");

        _root.Value = 20;
        var operation = builder.Complete();

        Assert.That(operation, Is.Not.Null);
        Assert.That(operation, Is.InstanceOf<CustomOperation>());
        Assert.That(operation.Description, Is.EqualTo("Value Change"));
    }

    [Test]
    public void StateCapturingOperationBuilder_Complete_ShouldSetSequenceNumber()
    {
        var generator = new OperationSequenceGenerator();
        var builder = CustomOperation.CaptureState(
            () => _root.Value,
            value => _root.Value = value,
            generator);

        var operation = builder.Complete();

        Assert.That(operation.SequenceNumber, Is.EqualTo(1));
    }

    [Test]
    public void StateCapturingOperationBuilder_Complete_Apply_ShouldRestoreAfterState()
    {
        _root.Value = 5;
        var builder = CustomOperation.CaptureState(
            () => _root.Value,
            value => _root.Value = value,
            _sequenceGenerator);

        _root.Value = 15;
        var operation = builder.Complete();

        _root.Value = 999;

        operation.Apply(_context);

        Assert.That(_root.Value, Is.EqualTo(15));
    }

    [Test]
    public void StateCapturingOperationBuilder_Complete_Revert_ShouldRestoreBeforeState()
    {
        _root.Value = 25;
        var builder = CustomOperation.CaptureState(
            () => _root.Value,
            value => _root.Value = value,
            _sequenceGenerator);

        _root.Value = 75;
        var operation = builder.Complete();

        _root.Value = 999;

        operation.Revert(_context);

        Assert.That(_root.Value, Is.EqualTo(25));
    }

    [Test]
    public void StateCapturingOperationBuilder_ShouldWorkWithComplexTypes()
    {
        _root.Name = "Original";
        var builder = CustomOperation.CaptureState(
            () => _root.Name,
            value => _root.Name = value,
            _sequenceGenerator,
            "Name Change");

        _root.Name = "Modified";
        var operation = builder.Complete();

        _root.Name = "Something Else";

        operation.Apply(_context);
        Assert.That(_root.Name, Is.EqualTo("Modified"));

        operation.Revert(_context);
        Assert.That(_root.Name, Is.EqualTo("Original"));
    }

    [Test]
    public void StateCapturingOperationBuilder_ShouldWorkWithNullValues()
    {
        _root.Name = null;
        var builder = CustomOperation.CaptureState(
            () => _root.Name,
            value => _root.Name = value,
            _sequenceGenerator);

        _root.Name = "Not Null";
        var operation = builder.Complete();

        operation.Revert(_context);
        Assert.That(_root.Name, Is.Null);

        operation.Apply(_context);
        Assert.That(_root.Name, Is.EqualTo("Not Null"));
    }

    [Test]
    public void StateCapturingOperationBuilder_Complete_ShouldWorkWithNoStateChange()
    {
        _root.Value = 100;
        var builder = CustomOperation.CaptureState(
            () => _root.Value,
            value => _root.Value = value,
            _sequenceGenerator);

        var operation = builder.Complete();

        _root.Value = 999;

        operation.Apply(_context);
        Assert.That(_root.Value, Is.EqualTo(100));

        operation.Revert(_context);
        Assert.That(_root.Value, Is.EqualTo(100));
    }

    #endregion

    #region Integration Tests

    [Test]
    public void CustomOperation_ShouldWorkWithContextFindObject()
    {
        // Use root object since it's always findable by context
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

        operation.Apply(_context);

        Assert.That(_root.Value, Is.EqualTo(500));

        operation.Revert(_context);
        Assert.That(_root.Value, Is.EqualTo(0));
    }

    [Test]
    public void SequenceNumber_ShouldBeUniqueAcrossOperations()
    {
        var generator = new OperationSequenceGenerator();
        var operations = new List<CustomOperation>();

        for (int i = 0; i < 10; i++)
        {
            operations.Add(CustomOperation.Create(
                () => { },
                () => { },
                generator,
                $"Op {i}"));
        }

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
