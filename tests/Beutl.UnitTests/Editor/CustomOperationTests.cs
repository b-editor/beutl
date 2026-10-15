using Beutl.Editor;
using Beutl.Editor.Operations;

namespace Beutl.UnitTests.Editor;

public class CustomOperationTests
{
    private TestCoreObject _root = null!;
    private OperationSequenceGenerator _sequenceGenerator = null!;

    [SetUp]
    public void Setup()
    {
        _root = new TestCoreObject();
        _sequenceGenerator = new OperationSequenceGenerator();
    }

    #region Constructor Tests

    [Test]
    public void Constructor_ShouldCreateInstance_WithValidArguments()
    {
        Action apply = () => { };
        Action revert = () => { };

        var operation = new CustomOperation(apply, revert)
        {
            SequenceNumber = 1
        };

        Assert.That(operation, Is.Not.Null);
        Assert.That(operation.SequenceNumber, Is.EqualTo(1));
    }

    [Test]
    public void Constructor_ShouldThrowArgumentNullException_WhenApplyIsNull()
    {
        Action revert = () => { };

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CustomOperation(null!, revert) { SequenceNumber = 1 });

        Assert.That(exception!.ParamName, Is.EqualTo("apply"));
    }

    [Test]
    public void Constructor_ShouldThrowArgumentNullException_WhenRevertIsNull()
    {
        Action apply = () => { };

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CustomOperation(apply, null!) { SequenceNumber = 1 });

        Assert.That(exception!.ParamName, Is.EqualTo("revert"));
    }

    #endregion

    #region Revert Tests

    [Test]
    public void ApplyThenRevert_ShouldRestoreOriginalState()
    {
        _root.Value = 50;
        var operation = new CustomOperation(
            () => _root.Value = 200,
            () => _root.Value = 50)
        {
            SequenceNumber = 1
        };

        operation.Apply();
        Assert.That(_root.Value, Is.EqualTo(200));
        operation.Revert();

        Assert.That(_root.Value, Is.EqualTo(50));
    }

    #endregion

    #region Create Static Method Tests

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
            _sequenceGenerator);

        operation.Apply();

        Assert.That(_root.Value, Is.EqualTo(999));

        operation.Revert();
        Assert.That(_root.Value, Is.EqualTo(0));
    }

    #endregion

    #region Integration Tests

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
                generator));
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
