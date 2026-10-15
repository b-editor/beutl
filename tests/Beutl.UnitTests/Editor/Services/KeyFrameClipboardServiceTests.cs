using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class KeyFrameClipboardServiceTests
{
    private HistoryHarness _harness = null!;
    private OperationSequenceGenerator _sequence = null!;
    private HistoryManager _history = null!;
    private KeyFrameClipboardService _service = null!;

    [SetUp]
    public void Setup()
    {
        _harness = new HistoryHarness(new TestRoot());
        _sequence = _harness.Sequence;
        _history = _harness.History;
        _service = new KeyFrameClipboardService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    private sealed class TestRoot : CoreObject;

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new KeyFrameClipboardService(null!));
    }

    // -------- PasteAnimation --------

    [Test]
    public void PasteAnimation_InvalidJson_ReturnsInvalidJson_NoCommit()
    {
        var animation = new KeyFrameAnimation<double>();
        int before = _history.UndoCount;

        KeyFrameAnimationPasteOutcome outcome = _service.PasteAnimation(animation, "not json");

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(KeyFrameAnimationPasteOutcome.InvalidJson));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void PasteAnimation_MissingType_ReturnsMissingType()
    {
        var animation = new KeyFrameAnimation<double>();
        int before = _history.UndoCount;

        KeyFrameAnimationPasteOutcome outcome = _service.PasteAnimation(animation, "{}");

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(KeyFrameAnimationPasteOutcome.MissingType));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void PasteAnimation_GenericTypeMismatch_NoCommit()
    {
        // Target is double; clipboard payload is a float-typed animation.
        var animation = new KeyFrameAnimation<double>();
        var sourceFloat = new KeyFrameAnimation<float>();
        string json = CoreSerializer.SerializeToJsonString(sourceFloat);
        int before = _history.UndoCount;

        KeyFrameAnimationPasteOutcome outcome = _service.PasteAnimation(animation, json);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(KeyFrameAnimationPasteOutcome.GenericTypeMismatch));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void PasteAnimation_Matching_Pasted_AndCommits()
    {
        var animation = new KeyFrameAnimation<double>();
        // Observer makes the service's KeyFrames mutations visible to the HistoryManager.
        using var observer = new CoreObjectOperationObserver(null, animation, _sequence);
        _history.Subscribe(observer);

        var source = new KeyFrameAnimation<double>();
        source.KeyFrames.AddSorted(new KeyFrame<double> { KeyTime = TimeSpan.FromSeconds(1), Value = 0.5 });
        string json = CoreSerializer.SerializeToJsonString(source);
        int before = _history.UndoCount;

        KeyFrameAnimationPasteOutcome outcome = _service.PasteAnimation(animation, json);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(KeyFrameAnimationPasteOutcome.Pasted));
            Assert.That(animation.KeyFrames.Count, Is.EqualTo(1));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    // -------- PasteKeyFrame --------

    [Test]
    public void PasteKeyFrame_InvalidJson_ReturnsInvalidJson()
    {
        var animation = new KeyFrameAnimation<double>();

        KeyFramePasteResult result = _service.PasteKeyFrame(animation, "not json", TimeSpan.FromSeconds(1));

        Assert.That(result.Outcome, Is.EqualTo(KeyFramePasteOutcome.InvalidJson));
    }

    [Test]
    public void PasteKeyFrame_GenericTypeMismatch_ReturnsEasingForFallback_NoCommit()
    {
        var animation = new KeyFrameAnimation<double>();
        var sourceKey = new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(1),
            Value = 0f,
            Easing = new LinearEasing(),
        };
        string json = CoreSerializer.SerializeToJsonString(sourceKey);
        int before = _history.UndoCount;

        KeyFramePasteResult result = _service.PasteKeyFrame(animation, json, TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(KeyFramePasteOutcome.GenericTypeMismatch));
            Assert.That(result.EasingForFallback, Is.Not.Null);
            // No commit here: the caller's InsertKeyFrame fallback owns the commit boundary.
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void PasteKeyFrame_NewTime_Inserts_AndCommits()
    {
        var animation = new KeyFrameAnimation<double>();
        using var observer = new CoreObjectOperationObserver(null, animation, _sequence);
        _history.Subscribe(observer);

        var sourceKey = new KeyFrame<double>
        {
            KeyTime = TimeSpan.Zero,
            Value = 0.75,
            Easing = new LinearEasing(),
        };
        string json = CoreSerializer.SerializeToJsonString(sourceKey);
        int before = _history.UndoCount;

        KeyFramePasteResult result = _service.PasteKeyFrame(animation, json, TimeSpan.FromSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(KeyFramePasteOutcome.Inserted));
            Assert.That(animation.KeyFrames.Count, Is.EqualTo(1));
            Assert.That(animation.KeyFrames[0].KeyTime, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void PasteKeyFrame_ExistingTime_ReplacesExisting_AndCommits()
    {
        var animation = new KeyFrameAnimation<double>();
        using var observer = new CoreObjectOperationObserver(null, animation, _sequence);
        _history.Subscribe(observer);

        // Seed an existing keyframe at t=2s with a known value.
        var existing = new KeyFrame<double>
        {
            KeyTime = TimeSpan.FromSeconds(2),
            Value = 0.0,
            Easing = new LinearEasing(),
        };
        animation.KeyFrames.AddSorted(existing);
        _history.Commit("seed"); // seal so the next paste is a fresh entry

        // Paste a different value at the same time.
        var sourceKey = new KeyFrame<double>
        {
            KeyTime = TimeSpan.FromSeconds(99), // ignored — service uses keyTime arg
            Value = 0.5,
            Easing = new LinearEasing(),
        };
        string json = CoreSerializer.SerializeToJsonString(sourceKey);
        int before = _history.UndoCount;

        KeyFramePasteResult result = _service.PasteKeyFrame(animation, json, TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(KeyFramePasteOutcome.ReplacedExisting));
            Assert.That(animation.KeyFrames.Count, Is.EqualTo(1));
            Assert.That(((KeyFrame<double>)animation.KeyFrames[0]).Value, Is.EqualTo(0.5));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }
}
