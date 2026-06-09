using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementObjectServiceTests
{
    private string _basePath = null!;
    private Element _element = null!;
    private HistoryManager _history = null!;
    private CoreObjectOperationObserver _observer = null!;
    private ElementObjectService _service = null!;

    [SetUp]
    public void Setup()
    {
        _basePath = Path.Combine(Path.GetTempPath(), $"beutl_obj_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_basePath);

        _element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(2),
            ZIndex = 0,
            Uri = new Uri(Path.Combine(_basePath, $"{Guid.NewGuid():N}.belm")),
        };
        var sequence = new OperationSequenceGenerator();
        _history = new HistoryManager(_element, sequence);
        _observer = new CoreObjectOperationObserver(null, _element, sequence);
        _history.Subscribe(_observer);
        _service = new ElementObjectService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _observer.Dispose();
        _history.Dispose();
        if (Directory.Exists(_basePath)) Directory.Delete(_basePath, recursive: true);
    }

    private sealed class TestEngineObject : EngineObject;

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ElementObjectService(null!));
    }

    [Test]
    public void Add_AppendsAndCommits()
    {
        var obj = new TestEngineObject();
        int before = _history.UndoCount;

        _service.Add(_element, obj);

        Assert.Multiple(() =>
        {
            Assert.That(_element.Objects, Does.Contain(obj));
            Assert.That(_element.Objects[^1], Is.SameAs(obj));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void InsertAt_ClampsIndexAboveCount()
    {
        _service.Add(_element, new TestEngineObject());
        _service.Add(_element, new TestEngineObject());
        var inserted = new TestEngineObject();
        int before = _history.UndoCount;

        // Out-of-range index (drop at list bottom) must clamp to Count, not throw.
        _service.InsertAt(_element, 99, inserted);

        Assert.Multiple(() =>
        {
            Assert.That(_element.Objects[^1], Is.SameAs(inserted));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Remove_NotPresent_NoCommit()
    {
        var stray = new TestEngineObject();
        int before = _history.UndoCount;

        bool removed = _service.Remove(_element, stray);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Remove_Present_RemovesAndCommits()
    {
        var obj = new TestEngineObject();
        _service.Add(_element, obj);
        int before = _history.UndoCount;

        bool removed = _service.Remove(_element, obj);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(_element.Objects, Does.Not.Contain(obj));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Move_SameIndex_NoOp()
    {
        _service.Add(_element, new TestEngineObject());
        int before = _history.UndoCount;

        bool moved = _service.Move(_element, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Move_DifferentIndices_ReordersAndCommits()
    {
        var a = new TestEngineObject();
        var b = new TestEngineObject();
        _service.Add(_element, a);
        _service.Add(_element, b);
        int before = _history.UndoCount;

        bool moved = _service.Move(_element, 0, 1);

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True);
            Assert.That(_element.Objects[0], Is.SameAs(b));
            Assert.That(_element.Objects[1], Is.SameAs(a));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void PasteOver_InvalidJson_ReturnsInvalidJson_NoCommit()
    {
        _service.Add(_element, new TestEngineObject());
        int before = _history.UndoCount;

        ObjectPasteOutcome outcome = _service.PasteOver(_element, 0, "not json");

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ObjectPasteOutcome.InvalidJson));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void PasteOver_MissingType_ReturnsMissingType_NoCommit()
    {
        _service.Add(_element, new TestEngineObject());
        int before = _history.UndoCount;

        ObjectPasteOutcome outcome = _service.PasteOver(_element, 0, "{}");

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ObjectPasteOutcome.MissingType));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void PasteOver_ValidJson_ReplacesAndCommits()
    {
        var original = new TestEngineObject();
        _service.Add(_element, original);
        // Serialize a fresh instance with the proper $type discriminator.
        string json = CoreSerializer.SerializeToJsonString(new TestEngineObject());
        int before = _history.UndoCount;

        ObjectPasteOutcome outcome = _service.PasteOver(_element, 0, json);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ObjectPasteOutcome.Pasted));
            Assert.That(_element.Objects[0], Is.Not.SameAs(original));
            Assert.That(_element.Objects[0], Is.InstanceOf<TestEngineObject>());
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void SetEnabled_NoChange_NoCommit()
    {
        var obj = new TestEngineObject { IsEnabled = true };
        _service.Add(_element, obj);
        int before = _history.UndoCount;

        bool changed = _service.SetEnabled(obj, true);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void SetEnabled_DifferentValue_CommitsOnce()
    {
        var obj = new TestEngineObject { IsEnabled = true };
        _service.Add(_element, obj);
        int before = _history.UndoCount;

        bool changed = _service.SetEnabled(obj, false);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(obj.IsEnabled, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }
}
