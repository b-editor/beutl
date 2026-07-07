using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementResizeServiceTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private ElementResizeService _service = null!;

    [SetUp]
    public void Setup()
    {
        _harness = new SceneHistoryHarness("beutl_resize", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(30));
        _scene = _harness.Scene;
        _history = _harness.History;
        _service = new ElementResizeService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    private Element AddElement(TimeSpan start, TimeSpan length, int zIndex = 0)
        => _harness.AddElement(start, length, zIndex);

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

    [Test]
    public void Resize_RippleOn_NegativeStart_ThrowsBeforeMutation()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.Resize(_scene,
                [new ElementResizeRequest(element, TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(2), 0)],
                ripple: true));

        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Resize_RippleOn_ZeroLength_ThrowsBeforeMutation()
    {
        Element element = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.Resize(_scene,
                [new ElementResizeRequest(element, TimeSpan.FromSeconds(1), TimeSpan.Zero, 0)],
                ripple: true));

        Assert.Multiple(() =>
        {
            Assert.That(element.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(element.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }
}
