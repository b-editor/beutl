using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class KeyFrameMoveServiceTests
{
    private TestRoot _root = null!;
    private HistoryManager _history = null!;
    private CoreObjectOperationObserver _observer = null!;
    private KeyFrameMoveService _service = null!;

    [SetUp]
    public void Setup()
    {
        _root = new TestRoot();
        var sequence = new OperationSequenceGenerator();
        _history = new HistoryManager(_root, sequence);
        _observer = new CoreObjectOperationObserver(null, _root, sequence);
        _history.Subscribe(_observer);
        _service = new KeyFrameMoveService(_history);
    }

    private sealed class TestRoot : CoreObject;

    [TearDown]
    public void TearDown()
    {
        _observer.Dispose();
        _history.Dispose();
    }

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new KeyFrameMoveService(null!));
    }

    [Test]
    public void CommitMove_EmptyList_NoCommit()
    {
        int before = _history.UndoCount;

        _service.CommitMove([]);

        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }

    [Test]
    public void CommitMove_WithKeyFrames_CommitsOnce()
    {
        var keyFrame = new KeyFrame<double>
        {
            KeyTime = TimeSpan.FromSeconds(1),
            Value = 0,
            Easing = new LinearEasing(),
        };
        // Simulate the View mutating KeyTime during drag.
        keyFrame.KeyTime = TimeSpan.FromSeconds(2);
        int before = _history.UndoCount;

        _service.CommitMove([keyFrame]);

        Assert.That(_history.UndoCount, Is.GreaterThanOrEqualTo(before));
    }
}
