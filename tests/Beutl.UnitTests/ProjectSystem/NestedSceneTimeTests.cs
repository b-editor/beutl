using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using NUnit.Framework;

namespace Beutl.UnitTests.ProjectSystem;

// A Sound that records the time ranges it is asked to process and flush, so tests can assert which
// referenced-scene window an embedded SceneSound samples.
public sealed partial class RecordingSound : Sound
{
    internal static readonly List<TimeRange> ProcessedRanges = [];
    internal static readonly List<TimeRange> FlushedRanges = [];

    public RecordingSound() => ScanProperties<RecordingSound>();

    internal static void Reset()
    {
        ProcessedRanges.Clear();
        FlushedRanges.Clear();
    }

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        context.MarkAsOutput(context.AddNode(new RecordingNode()));
    }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;
    }

    private sealed class RecordingNode : AudioNode
    {
        // Lets the compose retain the sound's output node, so a flush reaches it.
        public override int GetLatencySamples(int sampleRate) => 240;

        public override AudioBuffer Process(AudioProcessContext context)
        {
            ProcessedRanges.Add(context.TimeRange);
            return new AudioBuffer(context.SampleRate, 2, context.GetSampleCount());
        }

        public override AudioBuffer Flush(AudioProcessContext context)
        {
            FlushedRanges.Add(context.TimeRange);
            return new AudioBuffer(context.SampleRate, 2, context.GetSampleCount());
        }
    }
}

// An embed (SceneDrawable / SceneSound) must evaluate the referenced scene over the same window the
// standalone player and renderer do (#3603): the element's local time maps to the referenced scene's
// window time.
[TestFixture]
public class NestedSceneTimeTests
{
    private string _basePath = null!;

    [SetUp]
    public void SetUp()
    {
        _basePath = Path.Combine(Path.GetTempPath(), $"beutl_nested_scene_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_basePath);
        RecordingSound.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        RecordingSound.Reset();
        if (Directory.Exists(_basePath)) Directory.Delete(_basePath, recursive: true);
    }

    private static MediaTime Seconds(double value) => MediaTime.FromSeconds(value);

    private Scene CreateScene(string name, MediaTime start, MediaTime duration)
    {
        var scene = new Scene(100, 100, name)
        {
            Uri = new Uri(Path.Combine(_basePath, $"{name}.scene")),
            Start = { Value = start },
            Duration = { Value = duration },
        };
        return scene;
    }

    private Element CreateElement(MediaTime start, MediaTime length, EngineObject obj)
    {
        var element = new Element
        {
            Start = { Value = start },
            Length = { Value = length },
            IsEnabled = { Value = true },
            Uri = new Uri(Path.Combine(_basePath, $"{Guid.NewGuid():N}.layer"))
        };
        element.AddObject(obj);
        return element;
    }

    // Width animated in element-local time, rising 100 per second: local t -> 100 + 100 * t.
    private static RectShape CreateAnimatedShape()
    {
        var shape = new RectShape();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.Zero },
            Value = { Value = 100 },
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = Seconds(10) },
            Value = { Value = 1100 },
        });
        shape.Width.SetKeyFrameAnimation(animation);
        return shape;
    }

    private static void Attach(Project project, Scene scene) => project.Items.Add(scene);

    [TestCase(0)]
    [TestCase(5)]
    public void SceneDrawableEmbed_PlaysTheReferencedSceneWindowFromTheElementStart(double startSeconds)
    {
        var project = new Project();
        _ = new BeutlApplication { Project = { Value = project } };

        // Scene B: a shape element at B-window [0, 10), with width animated by element-local time.
        Scene sceneB = CreateScene("b", Seconds(startSeconds), Seconds(10));
        Attach(project, sceneB);
        RectShape shape = CreateAnimatedShape();
        sceneB.Children.Add(CreateElement(Seconds(startSeconds), Seconds(10), shape));

        // Scene A: a SceneDrawable element at A-absolute [0, 10) referencing B.
        Scene sceneA = CreateScene("a", MediaTime.Zero, Seconds(10));
        Attach(project, sceneA);
        var drawable = new SceneDrawable { ReferencedScene = { Value = sceneB } };
        sceneA.Children.Add(CreateElement(MediaTime.Zero, Seconds(10), drawable));

        using var compositor = new SceneCompositor(sceneA) { DisableResourceShare = true, ForceOriginalSource = true };

        // Local t samples B-window t (B-absolute start + t), like the standalone player.
        Assert.Multiple(() =>
        {
            Assert.That(EmbeddedInnerWidth(compositor, Seconds(0.5)), Is.EqualTo(150),
                $"Embedded at A time 0.5s with Scene.Start {startSeconds}s.");
            Assert.That(EmbeddedInnerWidth(compositor, Seconds(5.5)), Is.EqualTo(650),
                $"Embedded at A time 5.5s with Scene.Start {startSeconds}s.");
        });
    }

    [TestCase(0)]
    [TestCase(5)]
    public void SceneDrawableEmbed_MatchesTheStandaloneSceneAtTheSameWindowTime(double startSeconds)
    {
        var project = new Project();
        _ = new BeutlApplication { Project = { Value = project } };

        Scene sceneB = CreateScene("b", Seconds(startSeconds), Seconds(10));
        Attach(project, sceneB);
        RectShape shape = CreateAnimatedShape();
        sceneB.Children.Add(CreateElement(Seconds(startSeconds), Seconds(10), shape));

        Scene sceneA = CreateScene("a", MediaTime.Zero, Seconds(10));
        Attach(project, sceneA);
        var drawable = new SceneDrawable { ReferencedScene = { Value = sceneB } };
        sceneA.Children.Add(CreateElement(MediaTime.Zero, Seconds(10), drawable));

        using var compositorA = new SceneCompositor(sceneA) { DisableResourceShare = true, ForceOriginalSource = true };
        using var compositorB = new SceneCompositor(sceneB) { DisableResourceShare = true, ForceOriginalSource = true };

        // The standalone scene evaluates its window [0, duration) as FromWindowTime(time).
        foreach (double windowSeconds in new[] { 0.5, 2.5, 8.5 })
        {
            CompositionFrame standalone = compositorB.EvaluateGraphics(Seconds(startSeconds + windowSeconds));
            float standaloneWidth = ((RectShape.Resource)standalone.Objects.Single()).Width;
            float embeddedWidth = EmbeddedInnerWidth(compositorA, Seconds(windowSeconds));
            Assert.That(embeddedWidth, Is.EqualTo(standaloneWidth),
                $"Embedded at local {windowSeconds}s must match standalone at B-window {windowSeconds}s.");
        }
    }

    [TestCase(0)]
    [TestCase(5)]
    public void SceneSoundEmbed_ComposesTheReferencedSceneWindowFromTheElementStart(double startSeconds)
    {
        var project = new Project();
        _ = new BeutlApplication { Project = { Value = project } };

        // Scene B: a sound element at B-window [0, 10).
        Scene sceneB = CreateScene("b", Seconds(startSeconds), Seconds(10));
        Attach(project, sceneB);
        sceneB.Children.Add(CreateElement(Seconds(startSeconds), Seconds(10), new RecordingSound()));

        // Scene A: a SceneSound element at A-absolute [0, 10) referencing B.
        Scene sceneA = CreateScene("a", MediaTime.Zero, Seconds(10));
        Attach(project, sceneA);
        var sceneSound = new SceneSound { ReferencedScene = { Value = sceneB } };
        sceneA.Children.Add(CreateElement(MediaTime.Zero, Seconds(10), sceneSound));

        using SceneComposer composer = new(sceneA, disableResourceShare: true, forceOriginalSource: true);

        Assert.Multiple(() =>
        {
            RecordingSound.Reset();
            composer.Compose(new TimeRange(MediaTime.Zero, Seconds(1)));
            Assert.That(RecordingSound.ProcessedRanges, Is.EqualTo([new TimeRange(Seconds(startSeconds), Seconds(1))]),
                "Embedded at A window [0, 1s) must process the referenced scene's B-absolute window.");

            RecordingSound.Reset();
            composer.Compose(new TimeRange(Seconds(5), Seconds(1)));
            Assert.That(RecordingSound.ProcessedRanges, Is.EqualTo([new TimeRange(Seconds(startSeconds + 5), Seconds(1))]),
                "Embedded at A window [5, 6s) must process the referenced scene's B-absolute range.");
        });
    }

    [TestCase(0)]
    [TestCase(5)]
    public void SceneSoundEmbed_MatchesTheStandaloneSceneAtTheSameWindowTime(double startSeconds)
    {
        var project = new Project();
        _ = new BeutlApplication { Project = { Value = project } };

        Scene sceneB = CreateScene("b", Seconds(startSeconds), Seconds(10));
        Attach(project, sceneB);
        sceneB.Children.Add(CreateElement(Seconds(startSeconds), Seconds(10), new RecordingSound()));

        Scene sceneA = CreateScene("a", MediaTime.Zero, Seconds(10));
        Attach(project, sceneA);
        var sceneSound = new SceneSound { ReferencedScene = { Value = sceneB } };
        sceneA.Children.Add(CreateElement(MediaTime.Zero, Seconds(10), sceneSound));

        using SceneComposer composerA = new(sceneA, disableResourceShare: true, forceOriginalSource: true);
        using SceneComposer composerB = new(sceneB, disableResourceShare: true, forceOriginalSource: true);

        foreach (double windowSeconds in new[] { 0.5, 2.5 })
        {
            RecordingSound.Reset();
            composerA.Compose(new TimeRange(Seconds(windowSeconds), Seconds(1)));
            TimeRange[] embedded = [.. RecordingSound.ProcessedRanges];

            RecordingSound.Reset();
            composerB.Compose(new TimeRange(Seconds(startSeconds + windowSeconds), Seconds(1)));
            TimeRange[] standalone = [.. RecordingSound.ProcessedRanges];

            Assert.That(embedded, Is.EqualTo(standalone).AsCollection,
                $"Embedded audio at A window {windowSeconds}s must match standalone at B-window {windowSeconds}s.");
        }
    }

    // A tail drain flows through the scene node like a compose, so a non-zero referenced Start must
    // shift the drained window the same way or the tail loses its samples.
    [TestCase(0)]
    [TestCase(5)]
    public void SceneSoundEmbed_ShiftsTheTailDrainLikeTheComposeWindow(double startSeconds)
    {
        var project = new Project();
        _ = new BeutlApplication { Project = { Value = project } };

        Scene sceneB = CreateScene("b", Seconds(startSeconds), Seconds(10));
        Attach(project, sceneB);
        sceneB.Children.Add(CreateElement(Seconds(startSeconds), Seconds(10), new RecordingSound()));

        Scene sceneA = CreateScene("a", MediaTime.Zero, Seconds(10));
        Attach(project, sceneA);
        var sceneSound = new SceneSound { ReferencedScene = { Value = sceneB } };
        sceneA.Children.Add(CreateElement(MediaTime.Zero, Seconds(10), sceneSound));

        using SceneComposer composer = new(sceneA, disableResourceShare: true, forceOriginalSource: true);
        composer.Compose(new TimeRange(MediaTime.Zero, Seconds(1)));

        composer.Flush(new TimeRange(Seconds(1), Seconds(1)), new CompositionEligibility([sceneSound]));

        Assert.Multiple(() =>
        {
            Assert.That(RecordingSound.ProcessedRanges, Is.EqualTo([new TimeRange(Seconds(startSeconds), Seconds(1))]));
            Assert.That(RecordingSound.FlushedRanges, Has.Count.EqualTo(1));
            Assert.That(RecordingSound.FlushedRanges[0].Start, Is.EqualTo(Seconds(startSeconds + 1)),
                "The tail must be drained at the referenced scene's shifted window.");
        });
    }

    private static float EmbeddedInnerWidth(SceneCompositor compositor, MediaTime time)
    {
        CompositionFrame outer = compositor.EvaluateGraphics(time);
        var sceneDrawableResource = (SceneDrawable.Resource)outer.Objects.Single();
        CompositionFrame? innerFrame = sceneDrawableResource.Frame;
        Assert.That(innerFrame, Is.Not.Null, "The inner frame was not evaluated.");
        Assert.That(innerFrame!.Value.Objects.Length, Is.EqualTo(1), "The inner scene had no active element.");
        return ((RectShape.Resource)innerFrame.Value.Objects[0]).Width;
    }
}
