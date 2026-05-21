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
    private OperationSequenceGenerator _sequence = null!;
    private HistoryManager _history = null!;
    private CoreObjectOperationObserver _rootObserver = null!;
    private KeyFrameMoveService _service = null!;

    [SetUp]
    public void Setup()
    {
        _root = new TestRoot();
        _sequence = new OperationSequenceGenerator();
        _history = new HistoryManager(_root, _sequence);
        _rootObserver = new CoreObjectOperationObserver(null, _root, _sequence);
        _history.Subscribe(_rootObserver);
        _service = new KeyFrameMoveService(_history);
    }

    private sealed class TestRoot : CoreObject;

    [TearDown]
    public void TearDown()
    {
        _rootObserver.Dispose();
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
    public void CommitMove_WithObservedKeyFrameEdit_RecordsOneEntry()
    {
        // The View drives KeyTime during drag. To exercise the commit
        // boundary, the KeyFrame must be observed by something subscribed to
        // the HistoryManager — otherwise the property change is invisible
        // and Commit silently no-ops.
        var keyFrame = new KeyFrame<double>
        {
            KeyTime = TimeSpan.FromSeconds(1),
            Value = 0,
            Easing = new LinearEasing(),
        };
        using var keyFrameObserver = new CoreObjectOperationObserver(null, keyFrame, _sequence);
        _history.Subscribe(keyFrameObserver);

        int before = _history.UndoCount;

        // Simulate the View mutating KeyTime during drag.
        keyFrame.KeyTime = TimeSpan.FromSeconds(2);
        _service.CommitMove([keyFrame]);

        Assert.Multiple(() =>
        {
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
            Assert.That(keyFrame.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void CommitMove_AfterUndo_RestoresOriginalKeyTime()
    {
        var keyFrame = new KeyFrame<double>
        {
            KeyTime = TimeSpan.FromSeconds(1),
            Value = 0,
            Easing = new LinearEasing(),
        };
        using var keyFrameObserver = new CoreObjectOperationObserver(null, keyFrame, _sequence);
        _history.Subscribe(keyFrameObserver);

        keyFrame.KeyTime = TimeSpan.FromSeconds(2);
        _service.CommitMove([keyFrame]);

        _history.Undo();

        Assert.That(keyFrame.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }
}
