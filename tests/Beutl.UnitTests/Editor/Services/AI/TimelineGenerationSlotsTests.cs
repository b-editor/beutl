using Beutl.Editor.Services.AI;
using Beutl.Media;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services.AI;

[TestFixture]
public sealed class TimelineGenerationSlotsTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new SceneHistoryHarness("beutl_ai_slots", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(120));
        _scene = _harness.Scene;
    }

    [TearDown]
    public void TearDown() => _harness.Dispose();

    private static TimeRange Seconds(double start, double length)
        => new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(length));

    [Test]
    public void AStretchWithAnElementIsNotFree()
    {
        _harness.AddElement(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), zIndex: 1);

        Assert.Multiple(() =>
        {
            Assert.That(TimelineGenerationSlots.IsFree(_scene, 1, Seconds(3, 4)), Is.False);
            Assert.That(TimelineGenerationSlots.IsFree(_scene, 1, Seconds(4, 4)), Is.True, "touching is not overlapping");
            Assert.That(TimelineGenerationSlots.IsFree(_scene, 0, Seconds(3, 4)), Is.True);
        });
    }

    [Test]
    public void AnotherGenerationsStretchIsNotFree()
    {
        TimelineGenerationSlot[] reserved = [new(2, Seconds(10, 6))];

        Assert.That(TimelineGenerationSlots.IsFree(_scene, 2, Seconds(12, 1), reserved), Is.False);
        Assert.That(TimelineGenerationSlots.FindFreeLayer(_scene, Seconds(12, 1), 2, reserved: reserved), Is.EqualTo(3));
    }

    [Test]
    public void ALockedLayerIsNeverFree()
    {
        _harness.AddLayer(3).IsLocked = true;

        Assert.That(TimelineGenerationSlots.IsFree(_scene, 3, Seconds(0, 1)), Is.False);
        Assert.That(TimelineGenerationSlots.FindFreeLayer(_scene, Seconds(0, 1), 3), Is.EqualTo(4));
    }

    [Test]
    public void AClipWithSoundNeedsTwoFreeLayers()
    {
        _harness.AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(10), zIndex: 1);

        Assert.That(TimelineGenerationSlots.FindFreeLayer(_scene, Seconds(5, 2), 0, span: 2), Is.EqualTo(2));
        Assert.That(TimelineGenerationSlots.FindFreeLayer(_scene, Seconds(5, 2), 0, span: 1), Is.EqualTo(0));
    }

    [Test]
    public void AGapRunsFromThePreviousClipToTheNext()
    {
        Element previous = _harness.AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(4), zIndex: 0);
        _harness.AddElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2), zIndex: 0);

        TimelineGap? gap = TimelineGenerationSlots.FindGap(_scene, 0, TimeSpan.FromSeconds(6));

        Assert.That(gap, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(gap!.Start, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(gap.MaxLength, Is.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(gap.Previous, Is.SameAs(previous));
        });
    }

    [Test]
    public void TheSpaceAfterTheLastClipHasNoEnd()
    {
        _harness.AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(4), zIndex: 0);

        TimelineGap? gap = TimelineGenerationSlots.FindGap(_scene, 0, TimeSpan.FromSeconds(30));

        Assert.That(gap?.Start, Is.EqualTo(TimeSpan.FromSeconds(4)));
        Assert.That(gap?.MaxLength, Is.Null);
    }

    [Test]
    public void BeforeTheFirstClipTheGapStartsWhereClicked()
    {
        _harness.AddElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(4), zIndex: 0);

        TimelineGap? gap = TimelineGenerationSlots.FindGap(_scene, 0, TimeSpan.FromSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(gap?.Start, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(gap?.MaxLength, Is.EqualTo(TimeSpan.FromSeconds(7)));
            Assert.That(gap?.Previous, Is.Null);
        });
    }

    [Test]
    public void NoGapUnderAClipOrAGeneration()
    {
        _harness.AddElement(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(4), zIndex: 0);
        TimelineGenerationSlot[] reserved = [new(0, Seconds(4, 6))];

        Assert.That(TimelineGenerationSlots.FindGap(_scene, 0, TimeSpan.FromSeconds(2)), Is.Null);
        Assert.That(TimelineGenerationSlots.FindGap(_scene, 0, TimeSpan.FromSeconds(5), reserved), Is.Null);
        Assert.That(TimelineGenerationSlots.FindGap(_scene, 0, TimeSpan.FromSeconds(12), reserved)?.Start,
            Is.EqualTo(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public void TheSpecRoundTripsThroughItsSavedForm()
    {
        var spec = new TimelineGenerationSpec
        {
            Kind = TimelineGenerationKind.ImageEdit,
            ImageTask = AiImageEditTask.Outpaint,
            Prompt = "a wider beach",
            ModelId = "model",
            OutpaintExpansionPercent = 50,
        };

        TimelineGenerationSpec? restored = TimelineGenerationSpec.FromJson(spec.ToJson());

        Assert.That(restored, Is.EqualTo(spec));
        Assert.That(restored!.OperationId, Is.EqualTo("image.edit.outpaint"));
        Assert.That(TimelineGenerationSpec.FromJson("not json"), Is.Null);
    }

    [Test]
    public void OnlyWhatTheRequestSendsChangesTheFingerprint()
    {
        var removeBackground = new TimelineGenerationSpec
        {
            Kind = TimelineGenerationKind.ImageEdit,
            ImageTask = AiImageEditTask.RemoveBackground,
        };

        Assert.Multiple(() =>
        {
            Assert.That(
                (removeBackground with { Prompt = "ignored", DurationSeconds = 8 }).ComputeParameterFingerprint(),
                Is.EqualTo(removeBackground.ComputeParameterFingerprint()));
            Assert.That(
                (removeBackground with { ModelId = "other" }).ComputeParameterFingerprint(),
                Is.Not.EqualTo(removeBackground.ComputeParameterFingerprint()));
        });
    }

    [Test]
    public void ARequestNeedsItsPromptAndItsSource()
    {
        var video = new TimelineGenerationSpec { Kind = TimelineGenerationKind.Video };
        var edit = new TimelineGenerationSpec { Kind = TimelineGenerationKind.VideoEdit, Prompt = "night" };

        Assert.Throws<GenerativeExecutionException>(
            () => TimelineGenerationRequests.Build(video, new TimelineGenerationInputs(), "seed"));
        Assert.Throws<GenerativeExecutionException>(
            () => TimelineGenerationRequests.Build(edit, new TimelineGenerationInputs(), "seed"));
        GenerativeRequest request = TimelineGenerationRequests.Build(
            video with { Prompt = "waves", Resolution = "1080p", AspectRatio = "9:16" },
            new TimelineGenerationInputs(),
            "seed");
        Assert.That(request, Is.TypeOf<AiVideoGenerationNodeRequest>());
        Assert.That(((AiVideoGenerationNodeRequest)request).AspectRatio, Is.EqualTo("9:16"));
        Assert.That(request.CatalogOperationId, Is.EqualTo("video.generate"));
    }
}
