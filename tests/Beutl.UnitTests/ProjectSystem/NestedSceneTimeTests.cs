using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;
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
            return new AudioBuffer(context.SampleRate, LastProcessedChannelCount, context.GetSampleCount());
        }

        public override AudioBuffer Flush(AudioProcessContext context)
        {
            FlushedRanges.Add(context.TimeRange);
            return new AudioBuffer(context.SampleRate, LastProcessedChannelCount, context.GetSampleCount());
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

    [TestCase(null, false)]
    [TestCase(null, true)]
    [TestCase(SilenceSplitMode.SplitOnly, false)]
    [TestCase(SilenceSplitMode.SplitOnly, true)]
    [TestCase(SilenceSplitMode.DeleteSilence, false)]
    [TestCase(SilenceSplitMode.DeleteSilence, true)]
    [TestCase(SilenceSplitMode.RippleDelete, false)]
    [TestCase(SilenceSplitMode.RippleDelete, true)]
    public void Split_PreservesReferencedVideoAndAudioTogether(SilenceSplitMode? mode, bool linked)
    {
        using var h = new SceneHistoryHarness("scene-split", duration: Seconds(30));
        var project = new Project { Items = { h.Scene } };
        _ = new BeutlApplication { Project = { Value = project } };
        Scene referenced = CreateScene("referenced", Seconds(5), Seconds(10));
        Attach(project, referenced);
        RectShape shape = CreateAnimatedShape();
        var animation = shape.Width.GetKeyFrameAnimation()!;
        // Use referenced-scene time as the probe, including after a file-backed reference is loaded.
        animation.UseGlobalClock.Value = true;
        foreach (IKeyFrame key in animation.KeyFrames) key.KeyTime.Value += referenced.Start.Value;
        referenced.Children.Add(CreateElement(Seconds(5), Seconds(10), shape));
        referenced.Children.Add(CreateElement(Seconds(5), Seconds(10), new RecordingSound()));
        CoreSerializer.StoreToUri(referenced, referenced.Uri!);
        Element audio = h.AddElement(Seconds(10), Seconds(10));
        audio.Objects.Add(new SceneSound { ReferencedScene = { Value = referenced } });
        Element video = linked ? h.AddElement(Seconds(10), Seconds(10), 1) : audio;
        video.Objects.Add(new SceneDrawable { ReferencedScene = { Value = referenced } });
        if (linked) h.Scene.Groups.Add([audio.Id.Value, video.Id.Value]);
        h.History.Commit("Setup");
        int before = h.History.UndoCount;

        double[] sampleTimes = [17.25, 18.25, 19.75];
        var expected = sampleTimes.Select(Sample).ToArray();

        if (mode is { } selected)
            new SilenceSplitService(h.History).Apply(h.Scene, audio,
                [new TimeRange(Seconds(13), Seconds(1)), new TimeRange(Seconds(16), Seconds(1))], selected, true);
        else
            new ElementStructureService(h.History).Split(h.Scene, linked ? [audio, video] : [audio], Seconds(17));

        Assert.That(h.History.UndoCount, Is.EqualTo(before + 1));
        AssertSamples(mode == SilenceSplitMode.RippleDelete ? 2 : 0);
        h.History.Undo();
        Assert.That(h.Scene.Children.Count, Is.EqualTo(linked ? 2 : 1));
        AssertSamples(0);
        h.History.Redo();
        AssertSamples(mode == SilenceSplitMode.RippleDelete ? 2 : 0);

        (float Width, TimeRange[] Audio) Sample(double seconds)
        {
            using var compositor = new SceneCompositor(h.Scene) { DisableResourceShare = true, ForceOriginalSource = true };
            using var composer = new SceneComposer(h.Scene, disableResourceShare: true, forceOriginalSource: true);
            float width = EmbeddedInnerWidth(compositor, Seconds(seconds));
            RecordingSound.Reset();
            using var buffer = composer.Compose(new TimeRange(Seconds(seconds), Seconds(0.05)));
            Assert.That(RecordingSound.ProcessedRanges, Is.Not.Empty);
            return (width, RecordingSound.ProcessedRanges.ToArray());
        }

        void AssertSamples(double shift)
        {
            for (int i = 0; i < sampleTimes.Length; i++)
            {
                var actual = Sample(sampleTimes[i] - shift);
                Assert.Multiple(() =>
                {
                    Assert.That(actual.Width, Is.EqualTo(expected[i].Width), "Video must continue from the cut.");
                    Assert.That(actual.Audio, Is.EqualTo(expected[i].Audio), "Audio must sample the same referenced window.");
                });
            }
        }
    }

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

    [Test]
    public void SceneSoundEmbed_ReusesTheNodeButRebuildsItsComposerForANewLayout()
    {
        var project = new Project();
        _ = new BeutlApplication { Project = { Value = project } };
        Scene nested = CreateScene("nested", MediaTime.Zero, Seconds(2));
        Attach(project, nested);
        nested.Children.Add(CreateElement(MediaTime.Zero, Seconds(2), new RecordingSound()));
        var sound = new SceneSound
        {
            ReferencedScene = { Value = nested },
            TimeRange = { Value = new TimeRange(MediaTime.Zero, Seconds(2)) }
        };
        using var resource = sound.ToResource(CompositionContext.Default);
        using var stereo = new AudioContext(44100, AudioChannelLayout.Stereo);
        sound.Compose(stereo, resource);
        var sceneNode = stereo.Nodes.Single(n => n.GetType().Name == "SceneNode");
        var process = new AudioProcessContext(new TimeRange(MediaTime.Zero, Seconds(0.1)), 44100, new AnimationSampler(), null);
        using var first = stereo.GetOutputNodes().Single().Process(process);
        Assert.That(first.ChannelCount, Is.EqualTo(2));
        using var surround = new AudioContext(44100, AudioChannelLayout.Surround51);
        surround.BeginUpdate(stereo.Nodes.ToArray());
        sound.Compose(surround, resource);
        surround.EndUpdate();
        Assert.That(surround.Nodes.Single(n => n.GetType().Name == "SceneNode"), Is.SameAs(sceneNode));
        using var second = surround.GetOutputNodes().Single().Process(process);
        Assert.That(second.ChannelCount, Is.EqualTo(6));
        using var tail = sceneNode.Flush(new AudioProcessContext(new TimeRange(Seconds(0.1), Seconds(0.01)), 44100, new AnimationSampler(), null));
        Assert.That(tail.ChannelCount, Is.EqualTo(6));
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
