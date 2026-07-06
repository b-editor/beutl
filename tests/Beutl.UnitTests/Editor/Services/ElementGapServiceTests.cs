using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementGapServiceTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private ElementGapService _service = null!;

    [SetUp]
    public void Setup()
    {
        _harness = new SceneHistoryHarness("beutl_gap", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(30));
        _scene = _harness.Scene;
        _history = _harness.History;
        _service = new ElementGapService(_history);
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
        Assert.Throws<ArgumentNullException>(() => new ElementGapService(null!));
    }

    [Test]
    public void CloseGap_NullArguments_Throw()
    {
        Element placeholder = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => _service.CloseGap(null!, placeholder));
            Assert.Throws<ArgumentNullException>(() => _service.CloseGap(_scene, null!));
        });
    }

    [Test]
    public void CloseGap_ClosesGapAndCommitsOnce()
    {
        Element a = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element b = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        bool result = _service.CloseGap(_scene, a);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void CloseGap_NoGap_ReturnsFalse_NoCommit()
    {
        Element a = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        bool result = _service.CloseGap(_scene, a);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void CloseGap_AnchorIsLastElement_ReturnsFalse_NoCommit()
    {
        Element a = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element b = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        bool result = _service.CloseGap(_scene, b);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void CloseAllGaps_NullScene_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _service.CloseAllGaps(null!));
    }

    [Test]
    public void CloseAllGaps_ClosesAllAndCommitsOnce()
    {
        AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0);
        Element b = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), 0);
        Element c = AddElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2), 0);
        int before = _history.UndoCount;

        int closed = _service.CloseAllGaps(_scene);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(2));
            Assert.That(b.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(c.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void CloseAllGaps_NoGaps_ReturnsZero_NoCommit()
    {
        AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2));
        int before = _history.UndoCount;

        int closed = _service.CloseAllGaps(_scene);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(0));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void CloseGap_ThenUndo_RestoresLayout()
    {
        Element a = AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element b = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        TimeSpan originalBStart = b.Start;
        int before = _history.UndoCount;

        _service.CloseGap(_scene, a);
        _history.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(b.Start, Is.EqualTo(originalBStart));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void CloseAllGaps_ThenUndo_RestoresLayout()
    {
        AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0);
        Element b = AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), 0);
        Element c = AddElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2), 0);
        TimeSpan originalBStart = b.Start;
        TimeSpan originalCStart = c.Start;
        int before = _history.UndoCount;

        _service.CloseAllGaps(_scene);
        _history.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(b.Start, Is.EqualTo(originalBStart));
            Assert.That(c.Start, Is.EqualTo(originalCStart));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }
}
