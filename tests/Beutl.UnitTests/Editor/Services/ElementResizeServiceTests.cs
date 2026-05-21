using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementResizeServiceTests
{
    private string _basePath = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private CoreObjectOperationObserver _observer = null!;
    private ElementResizeService _service = null!;

    [SetUp]
    public void Setup()
    {
        _basePath = Path.Combine(Path.GetTempPath(), $"beutl_resize_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_basePath);

        _scene = new Scene(100, 100, string.Empty)
        {
            Uri = new Uri(Path.Combine(_basePath, "test.scene")),
            Start = TimeSpan.Zero,
            Duration = TimeSpan.FromSeconds(30),
        };
        var sequence = new OperationSequenceGenerator();
        _history = new HistoryManager(_scene, sequence);
        _observer = new CoreObjectOperationObserver(null, _scene, sequence);
        _history.Subscribe(_observer);
        _service = new ElementResizeService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _observer.Dispose();
        _history.Dispose();
        if (Directory.Exists(_basePath)) Directory.Delete(_basePath, recursive: true);
    }

    private Element AddElement(TimeSpan start, TimeSpan length, int zIndex = 0)
    {
        var element = new Element
        {
            Start = start,
            Length = length,
            ZIndex = zIndex,
            Uri = new Uri(Path.Combine(_basePath, $"{Guid.NewGuid():N}.layer")),
        };
        _scene.Children.Add(element);
        return element;
    }

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ElementResizeService(null!));
    }

    [Test]
    public void Resize_NullScene_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Resize(null!, []));
    }

    [Test]
    public void Resize_NullRequests_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Resize(_scene, null!));
    }

    [Test]
    public void Resize_SingleElement_AppliesNewSizeAndCommitsOnce()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        _service.Resize(_scene, [new ElementResizeRequest(element, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), 0)]);

        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Resize_MultipleElements_CommitsSingleHistoryEntry()
    {
        Element e1 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0);
        Element e2 = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 1);
        int before = _history.UndoCount;

        _service.Resize(_scene,
        [
            new ElementResizeRequest(e1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), 0),
            new ElementResizeRequest(e2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(6), 1),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(e1.Length, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(e2.Length, Is.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Resize_EmptyList_DoesNotCommit()
    {
        int before = _history.UndoCount;

        _service.Resize(_scene, []);

        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }

    [Test]
    public void Resize_ZIndexChange_AppliesAndCommits()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0);
        int before = _history.UndoCount;

        _service.Resize(_scene, [new ElementResizeRequest(element, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 2)]);

        Assert.Multiple(() =>
        {
            Assert.That(element.ZIndex, Is.EqualTo(2));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }
}
