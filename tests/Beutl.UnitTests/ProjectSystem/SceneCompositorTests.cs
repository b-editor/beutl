using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Audio;
using SkiaSharp;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class SceneCompositorTests
{
    private string _basePath = null!;

    [SetUp]
    public void SetUp()
    {
        _basePath = Path.Combine(Path.GetTempPath(), $"beutl_scene_compositor_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_basePath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_basePath)) Directory.Delete(_basePath, recursive: true);
    }

    private static Scene CreateScene(string basePath)
    {
        var scene = new Scene(100, 100, string.Empty)
        {
            Uri = new Uri(Path.Combine(basePath, "test.scene"))
        };
        return scene;
    }

    private static Element CreateElement(string basePath, bool isEnabled, EngineObject obj)
    {
        var element = new Element
        {
            Start = { Value = MediaTime.Zero },
            Length = { Value = MediaTime.FromSeconds(1) },
            IsEnabled = { Value = isEnabled },
            Uri = new Uri(Path.Combine(basePath, $"{Guid.NewGuid():N}.layer"))
        };
        element.AddObject(obj);
        return element;
    }

    private static TimelineLayer CreateLayer(int zIndex, bool solo = false, bool audioMuted = false, bool videoMuted = false)
    {
        return new TimelineLayer
        {
            ZIndex = { Value = zIndex },
            IsSolo = { Value = solo },
            IsAudioMuted = { Value = audioMuted },
            IsVideoMuted = { Value = videoMuted },
        };
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DetachedGraphicsResource_KeepsItsPathAliveUntilTheNextEvaluation(bool reattach)
    {
        Scene scene = CreateScene(_basePath);
        var application = new BeutlApplication { Project = { Value = new Project() } };
        application.Project.Value!.Items.Add(scene);
        var shape = new RectShape();
        Element element = CreateElement(_basePath, isEnabled: true, shape);
        scene.Children.Add(element);
        using var compositor = new SceneCompositor(scene);
        CompositionFrame frame = compositor.EvaluateGraphics(MediaTime.Zero);
        var resource = (RectShape.Resource)frame.Objects.Single();
        var geometry = resource.GetGeometry()!;
        SKPath path = geometry.GetCachedPath();

        // The UI can remove this object after the render thread obtains its native path.
        element.Objects.Clear();
        Assert.That(path.Handle, Is.Not.EqualTo(IntPtr.Zero),
            "Detaching must not release a path that the current frame is about to draw.");
        Assert.That(resource.IsDisposed, Is.False);
        using (var bitmap = new SKBitmap(100, 100))
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.Red })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawPath(path, paint);
            Assert.That(bitmap.GetPixel(50, 50), Is.EqualTo(SKColors.Red));
        }

        if (reattach)
            element.Objects.Add(shape);

        CompositionFrame next = compositor.EvaluateGraphics(MediaTime.Zero);
        Assert.That(resource.IsDisposed, Is.True);
        Assert.That(geometry.IsDisposed, Is.True);
        Assert.That(path.Handle, Is.EqualTo(IntPtr.Zero));
        if (reattach)
        {
            Assert.That(next.Objects.Single(), Is.Not.SameAs(resource));
            Assert.That(next.Objects.Single().IsDisposed, Is.False);
        }
        else
        {
            Assert.That(next.Objects, Is.Empty);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DetachedAudioResource_IsReleasedByTheNextEvaluationOrDisposal(bool disposeCompositor)
    {
        Scene scene = CreateScene(_basePath);
        var application = new BeutlApplication { Project = { Value = new Project() } };
        application.Project.Value!.Items.Add(scene);
        Element element = CreateElement(_basePath, isEnabled: true, new TestAudioObject());
        scene.Children.Add(element);
        using var compositor = new SceneCompositor(scene);
        var range = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1));
        EngineObject.Resource resource = compositor.EvaluateAudio(range).Objects.Single();

        scene.Children.Remove(element);
        Assert.That(resource.IsDisposed, Is.False);

        if (disposeCompositor)
            compositor.Dispose();
        else
            Assert.That(compositor.EvaluateAudio(range).Objects, Is.Empty);

        Assert.That(resource.IsDisposed, Is.True);
    }

    [Test]
    public async Task DetachingDuringDisposal_ReleasesEveryCapturedResource()
    {
        for (int iteration = 0; iteration < 128; iteration++)
        {
            Scene scene = CreateScene(_basePath);
            var application = new BeutlApplication { Project = { Value = new Project() } };
            application.Project.Value!.Items.Add(scene);
            Element element = CreateElement(_basePath, isEnabled: true, new TestGraphicsObject());
            for (int index = 0; index < 127; index++)
                element.Objects.Add(new TestGraphicsObject());
            scene.Children.Add(element);
            using var compositor = new SceneCompositor(scene);
            CompositionFrame frame = compositor.EvaluateGraphics(MediaTime.Zero);
            using var start = new Barrier(2);
            Task detach = Task.Run(() =>
            {
                if (!start.SignalAndWait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Disposal did not start.");
                element.Objects.Clear();
            });

            Assert.That(start.SignalAndWait(TimeSpan.FromSeconds(10)), Is.True);
            compositor.Dispose();
            await detach.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(frame.Objects.All(resource => resource.IsDisposed), Is.True,
                $"Detachment left an undisposed resource at iteration {iteration}.");
        }
    }

    [Test]
    public void DisposalCallbacks_CanDetachObjectsOnAnotherThread()
    {
        Scene scene = CreateScene(_basePath);
        var application = new BeutlApplication { Project = { Value = new Project() } };
        application.Project.Value!.Items.Add(scene);
        Element element = CreateElement(_basePath, isEnabled: true, new TestGraphicsObject());
        element.Objects.Add(new DisposalCallbackObject(() =>
            Task.Run(() => element.Objects.Clear()).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult()));
        scene.Children.Add(element);
        using var compositor = new SceneCompositor(scene);
        CompositionFrame frame = compositor.EvaluateGraphics(MediaTime.Zero);

        compositor.Dispose();

        Assert.That(frame.Objects.All(resource => resource.IsDisposed), Is.True);
        Assert.That(element.Objects, Is.Empty);
    }

    [Test]
    public void EvaluateGraphics_DisabledElement_IsExcluded()
    {
        Scene scene = CreateScene(_basePath);
        Element enabled = CreateElement(_basePath, isEnabled: true, new TestGraphicsObject());
        Element disabled = CreateElement(_basePath, isEnabled: false, new TestGraphicsObject());
        scene.Children.Add(enabled);
        scene.Children.Add(disabled);
        using var compositor = new SceneCompositor(scene);

        CompositionFrame frame = compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5));

        Assert.That(frame.Objects.Length, Is.EqualTo(1));
        Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(enabled.Objects[0]));
    }

    [Test]
    public void EvaluateAudio_DisabledElement_IsExcluded()
    {
        Scene scene = CreateScene(_basePath);
        Element enabled = CreateElement(_basePath, isEnabled: true, new TestAudioObject());
        Element disabled = CreateElement(_basePath, isEnabled: false, new TestAudioObject());
        scene.Children.Add(enabled);
        scene.Children.Add(disabled);
        using var compositor = new SceneCompositor(scene);

        CompositionFrame frame = compositor.EvaluateAudio(
            new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1)));
        CompositionEligibility eligibility = frame.Eligibility
            ?? throw new AssertionException("Audio evaluation must capture eligibility.");

        Assert.That(frame.Objects.Length, Is.EqualTo(1));
        Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(enabled.Objects[0]));
        Assert.That(eligibility.Contains(enabled.Objects[0]), Is.True);
        Assert.That(eligibility.Contains(disabled.Objects[0]), Is.False);
    }

    [Test]
    public void EvaluateAudio_OutOfRangeElement_RemainsEligible()
    {
        Scene scene = CreateScene(_basePath);
        Element element = CreateElement(_basePath, isEnabled: true, new TestAudioObject());
        scene.Children.Add(element);
        using var compositor = new SceneCompositor(scene);

        CompositionFrame frame = compositor.EvaluateAudio(
            new TimeRange(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1)));
        CompositionEligibility eligibility = frame.Eligibility
            ?? throw new AssertionException("Audio evaluation must capture eligibility.");

        Assert.That(frame.Objects, Is.Empty);
        Assert.That(eligibility.Contains(element.Objects[0]), Is.True,
            "Eligibility ignores time intersection so Composer can identify a natural clip end.");
    }

    [Test]
    public void EvaluateAudio_FlowConsumedSound_IsNotEligible()
    {
        Scene scene = CreateScene(_basePath);
        var group = new SoundGroup();
        Element groupElement = CreateElement(_basePath, isEnabled: true, group);
        groupElement.ZIndex.Value = 0;
        groupElement.Length.Value = MediaTime.FromSeconds(1);
        ((PortalObject)groupElement.Objects[0]).Count.Value = 1;

        var child = new LimiterTailSound();
        Element childElement = CreateElement(_basePath, isEnabled: true, child);
        childElement.ZIndex.Value = 1;
        childElement.Length.Value = MediaTime.FromSeconds(1);

        scene.Children.Add(groupElement);
        scene.Children.Add(childElement);
        using var compositor = new SceneCompositor(scene);

        CompositionFrame frame = compositor.EvaluateAudio(
            new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1)));
        CompositionEligibility eligibility = frame.Eligibility
            ?? throw new AssertionException("Audio evaluation must capture eligibility.");

        Assert.That(eligibility.Contains(group), Is.True);
        Assert.That(eligibility.Contains(child), Is.False,
            "A Sound consumed by SoundGroup flow must not remain eligible as a standalone tail entry.");
    }

    [Test]
    public void EvaluateAudio_FlowConsumedValueEqualSound_IsNotEligible()
    {
        Scene scene = CreateScene(_basePath);
        var group = new SoundGroup();
        var groupElement = new ValueEqualElement
        {
            Start = { Value = MediaTime.Zero },
            Length = { Value = MediaTime.FromSeconds(1) },
            ZIndex = { Value = 0 },
            Uri = new Uri(Path.Combine(_basePath, $"{Guid.NewGuid():N}.group.layer")),
        };
        groupElement.AddObject(group);
        ((PortalObject)groupElement.Objects[0]).Count.Value = 1;

        var child = new LimiterTailSound();
        var childElement = new ValueEqualElement
        {
            Start = { Value = MediaTime.Zero },
            Length = { Value = MediaTime.FromSeconds(1) },
            ZIndex = { Value = 1 },
            Uri = new Uri(Path.Combine(_basePath, $"{Guid.NewGuid():N}.child.layer")),
        };
        childElement.AddObject(child);

        scene.Children.Add(groupElement);
        scene.Children.Add(childElement);
        using var compositor = new SceneCompositor(scene);

        CompositionFrame frame = compositor.EvaluateAudio(
            new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1)));
        CompositionEligibility eligibility = frame.Eligibility
            ?? throw new AssertionException("Audio evaluation must capture eligibility.");

        Assert.That(eligibility.Contains(child), Is.False,
            "Reference-equal flow removal must still consume a value-equal standalone sound.");
    }

    [Test]
    public void EvaluateAudio_StandaloneClearPortal_ExcludesClearedSoundFromEligibility()
    {
        Scene scene = CreateScene(_basePath);
        var sound = new LimiterTailSound();
        Element element = CreateElement(_basePath, isEnabled: true, sound);
        var portal = new PortalObject();
        portal.Clear.Value = true;
        portal.Count.Value = 0;
        element.Objects.Add(portal);
        scene.Children.Add(element);
        using var compositor = new SceneCompositor(scene);

        CompositionFrame frame = compositor.EvaluateAudio(
            new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1)));
        CompositionEligibility eligibility = frame.Eligibility
            ?? throw new AssertionException("Audio evaluation must capture eligibility.");

        Assert.Multiple(() =>
        {
            Assert.That(frame.Objects.Select(resource => resource.GetOriginal()),
                Does.Not.Contain(sound),
                "A standalone clear portal removes the preceding sound from the evaluated flow.");
            Assert.That(eligibility.Contains(sound), Is.False,
                "Eligibility must mirror the portal's flow mutation so a cleared sound cannot flush a stale tail.");
        });
    }

    [Test]
    public void EvaluateAudio_InactiveFlowOperator_DoesNotConsumeEndedSound()
    {
        Scene scene = CreateScene(_basePath);
        var group = new SoundGroup();
        Element groupElement = CreateElement(_basePath, isEnabled: true, group);
        groupElement.Start.Value = MediaTime.FromSeconds(2);
        groupElement.ZIndex.Value = 0;
        groupElement.Length.Value = MediaTime.FromSeconds(1);
        ((PortalObject)groupElement.Objects[0]).Count.Value = 1;

        var child = new LimiterTailSound();
        Element childElement = CreateElement(_basePath, isEnabled: true, child);
        childElement.ZIndex.Value = 1;
        childElement.Length.Value = MediaTime.FromSeconds(1);

        scene.Children.Add(groupElement);
        scene.Children.Add(childElement);
        using var compositor = new SceneCompositor(scene);

        CompositionFrame frame = compositor.EvaluateAudio(
            new TimeRange(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1)));
        CompositionEligibility eligibility = frame.Eligibility
            ?? throw new AssertionException("Audio evaluation must capture eligibility.");

        Assert.That(eligibility.Contains(child), Is.True,
            "An inactive flow operator must not consume an ended standalone Sound during eligibility evaluation.");
    }

    [Test]
    public void EvaluateAudio_ActiveFlowOperator_DoesNotConsumeEndedSoundOutsideRange()
    {
        const int sampleRate = 48000;
        const int clipSamples = 4096;
        const int lookaheadSamples = 240;
        var duration = AudioProcessContext.GetDurationForSampleCount(clipSamples, sampleRate);
        var lookahead = AudioProcessContext.GetDurationForSampleCount(lookaheadSamples, sampleRate);
        Scene scene = CreateScene(_basePath);
        var group = new SoundGroup();
        Element groupElement = CreateElement(_basePath, isEnabled: true, group);
        groupElement.Start.Value = duration;
        groupElement.Length.Value = MediaTime.FromSeconds(1);
        groupElement.ZIndex.Value = 0;
        ((PortalObject)groupElement.Objects[0]).Count.Value = 1;

        var child = new LimiterTailSound { LookaheadMs = 5f };
        Element childElement = CreateElement(_basePath, isEnabled: true, child);
        childElement.Length.Value = duration;
        childElement.ZIndex.Value = 1;

        scene.Children.Add(groupElement);
        scene.Children.Add(childElement);
        using var compositor = new SceneCompositor(scene);
        using var composer = new Composer { SampleRate = sampleRate };

        var firstRange = new TimeRange(MediaTime.Zero, duration);
        using AudioBuffer? first = composer.Compose(firstRange, compositor.EvaluateAudio(firstRange));

        var tailRange = new TimeRange(duration, lookahead);
        CompositionFrame tailFrame = compositor.EvaluateAudio(tailRange);
        CompositionEligibility eligibility = tailFrame.Eligibility
            ?? throw new AssertionException("Audio evaluation must capture eligibility.");
        using AudioBuffer? tail = composer.Compose(tailRange, tailFrame);

        Assert.Multiple(() =>
        {
            Assert.That(eligibility.Contains(child), Is.True,
                "An active portal must not consume an ended sound that is outside the evaluated range.");
            Assert.That(tail, Is.Not.Null);
            Assert.That(tail!.GetChannelData(0)[..lookaheadSamples].ToArray(), Has.Some.Not.EqualTo(0f),
                "Keeping the ended sound eligible must preserve its retained tail for the following range.");
        });
    }

    [Test]
    public void EvaluateAudio_DoesNotMaterializeOutOfRangeAudioObjectsForEligibility()
    {
        Scene scene = CreateScene(_basePath);
        var inactive = new CountingAudioObject();
        Element element = CreateElement(_basePath, isEnabled: true, inactive);
        element.Start.Value = MediaTime.FromSeconds(2);
        element.Length.Value = MediaTime.FromSeconds(1);
        scene.Children.Add(element);
        using var compositor = new SceneCompositor(scene);

        _ = compositor.EvaluateAudio(new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1)));

        Assert.That(inactive.UpdateCount, Is.EqualTo(0),
            "Eligibility collection must not materialize an out-of-range audio resource.");
    }

    [Test]
    public void EvaluateAudio_ActiveFlowResourceIsUpdatedOnce()
    {
        Scene scene = CreateScene(_basePath);
        var counting = new CountingAudioObject();
        scene.Children.Add(CreateElement(_basePath, isEnabled: true, counting));
        using var compositor = new SceneCompositor(scene);

        CompositionFrame frame = compositor.EvaluateAudio(
            new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1)));

        Assert.That(frame.Objects.Select(resource => resource.GetOriginal()), Has.Member(counting));
        Assert.That(counting.UpdateCount, Is.EqualTo(1),
            "An active flow object must be updated once while producing both the frame and its eligibility.");
    }

    [Test]
    public void EvaluateGraphics_TogglingIsEnabled_UpdatesFrameContents()
    {
        Scene scene = CreateScene(_basePath);
        Element element = CreateElement(_basePath, isEnabled: true, new TestGraphicsObject());
        scene.Children.Add(element);
        using var compositor = new SceneCompositor(scene);
        var time = MediaTime.FromSeconds(0.5);

        Assert.That(compositor.EvaluateGraphics(time).Objects.Length, Is.EqualTo(1));

        element.IsEnabled.Value = false;
        Assert.That(compositor.EvaluateGraphics(time).Objects.Length, Is.EqualTo(0));

        element.IsEnabled.Value = true;
        Assert.That(compositor.EvaluateGraphics(time).Objects.Length, Is.EqualTo(1));
    }

    [Test]
    public void EvaluateAudio_TogglingIsEnabled_UpdatesFrameContents()
    {
        // Graphics と Audio の両方が同じ SortLayers を経由するため、
        // 有効/無効切り替え時に音声側も一貫してフィルタされることを確認する。
        Scene scene = CreateScene(_basePath);
        Element element = CreateElement(_basePath, isEnabled: true, new TestAudioObject());
        scene.Children.Add(element);
        using var compositor = new SceneCompositor(scene);
        var range = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1));

        Assert.That(compositor.EvaluateAudio(range).Objects.Length, Is.EqualTo(1));

        element.IsEnabled.Value = false;
        Assert.That(compositor.EvaluateAudio(range).Objects.Length, Is.EqualTo(0));
    }

    [Test]
    public void EvaluateGraphics_SoloedLayer_OnlySoloedContributes()
    {
        Scene scene = CreateScene(_basePath);
        Element layer0 = CreateElement(_basePath, isEnabled: true, new TestGraphicsObject());
        layer0.ZIndex.Value = 0;
        Element layer1 = CreateElement(_basePath, isEnabled: true, new TestGraphicsObject());
        layer1.ZIndex.Value = 1;
        scene.Children.Add(layer0);
        scene.Children.Add(layer1);
        scene.Layers.Add(CreateLayer(0));
        scene.Layers.Add(CreateLayer(1, solo: true));
        using var compositor = new SceneCompositor(scene);

        CompositionFrame frame = compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5));

        Assert.That(frame.Objects.Length, Is.EqualTo(1));
        Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(layer1.Objects[0]));
    }

    [Test]
    public void EvaluateAudio_MultipleSolos_AreNonExclusive()
    {
        Scene scene = CreateScene(_basePath);
        Element z0 = CreateElement(_basePath, isEnabled: true, new TestAudioObject());
        z0.ZIndex.Value = 0;
        Element z1 = CreateElement(_basePath, isEnabled: true, new TestAudioObject());
        z1.ZIndex.Value = 1;
        Element z2 = CreateElement(_basePath, isEnabled: true, new TestAudioObject());
        z2.ZIndex.Value = 2;
        scene.Children.Add(z0);
        scene.Children.Add(z1);
        scene.Children.Add(z2);
        scene.Layers.Add(CreateLayer(0, solo: true));
        scene.Layers.Add(CreateLayer(1));
        scene.Layers.Add(CreateLayer(2, solo: true));
        using var compositor = new SceneCompositor(scene);

        CompositionFrame frame = compositor.EvaluateAudio(
            new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1)));

        Assert.That(frame.Objects.Length, Is.EqualTo(2));
        Assert.That(frame.Objects.Select(o => o.GetOriginal()),
            Is.EquivalentTo(new[] { z0.Objects[0], z2.Objects[0] }));
    }

    [Test]
    public void EvaluateGraphics_Solo_LayerWithoutModelIsExcluded()
    {
        Scene scene = CreateScene(_basePath);
        Element soloed = CreateElement(_basePath, isEnabled: true, new TestGraphicsObject());
        soloed.ZIndex.Value = 0;
        Element modelless = CreateElement(_basePath, isEnabled: true, new TestGraphicsObject());
        modelless.ZIndex.Value = 5;
        scene.Children.Add(soloed);
        scene.Children.Add(modelless);
        scene.Layers.Add(CreateLayer(0, solo: true));
        using var compositor = new SceneCompositor(scene);

        CompositionFrame frame = compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5));

        Assert.That(frame.Objects.Length, Is.EqualTo(1));
        Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(soloed.Objects[0]));
    }

    [Test]
    public void EvaluateGraphics_VideoMutedLayer_ExcludedFromGraphics()
    {
        Scene scene = CreateScene(_basePath);
        Element z0 = CreateElement(_basePath, isEnabled: true, new TestGraphicsObject());
        z0.ZIndex.Value = 0;
        Element z1 = CreateElement(_basePath, isEnabled: true, new TestGraphicsObject());
        z1.ZIndex.Value = 1;
        scene.Children.Add(z0);
        scene.Children.Add(z1);
        scene.Layers.Add(CreateLayer(0, videoMuted: true));
        scene.Layers.Add(CreateLayer(1));
        using var compositor = new SceneCompositor(scene);

        CompositionFrame frame = compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5));

        Assert.That(frame.Objects.Length, Is.EqualTo(1));
        Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(z1.Objects[0]));
    }

    [Test]
    public void EvaluateAudio_VideoMutedLayer_StillComposesAudio()
    {
        Scene scene = CreateScene(_basePath);
        Element z0 = CreateElement(_basePath, isEnabled: true, new TestAudioObject());
        z0.ZIndex.Value = 0;
        scene.Children.Add(z0);
        scene.Layers.Add(CreateLayer(0, videoMuted: true));
        using var compositor = new SceneCompositor(scene);

        CompositionFrame frame = compositor.EvaluateAudio(
            new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1)));

        Assert.That(frame.Objects.Length, Is.EqualTo(1));
        Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(z0.Objects[0]));
    }

    [Test]
    public void EvaluateAudio_AudioMutedLayer_ExcludedFromAudio()
    {
        Scene scene = CreateScene(_basePath);
        Element z0 = CreateElement(_basePath, isEnabled: true, new TestAudioObject());
        z0.ZIndex.Value = 0;
        Element z1 = CreateElement(_basePath, isEnabled: true, new TestAudioObject());
        z1.ZIndex.Value = 1;
        scene.Children.Add(z0);
        scene.Children.Add(z1);
        scene.Layers.Add(CreateLayer(0, audioMuted: true));
        scene.Layers.Add(CreateLayer(1));
        using var compositor = new SceneCompositor(scene);

        CompositionFrame frame = compositor.EvaluateAudio(
            new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1)));
        CompositionEligibility eligibility = frame.Eligibility
            ?? throw new AssertionException("Audio evaluation must capture eligibility.");

        Assert.That(frame.Objects.Length, Is.EqualTo(1));
        Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(z1.Objects[0]));
        Assert.That(eligibility.Contains(z0.Objects[0]), Is.False);
        Assert.That(eligibility.Contains(z1.Objects[0]), Is.True);
    }

    [Test]
    public void CompositionEligibility_UsesReferenceIdentity()
    {
        var first = new ValueEqualEligibilityObject();
        var second = new ValueEqualEligibilityObject();
        var eligibility = new CompositionEligibility([first]);

        Assert.That(eligibility.Contains(first), Is.True);
        Assert.That(eligibility.Contains(second), Is.False);
    }

    [Test]
    public void EvaluateGraphics_AudioMutedLayer_StillComposesGraphics()
    {
        Scene scene = CreateScene(_basePath);
        Element z0 = CreateElement(_basePath, isEnabled: true, new TestGraphicsObject());
        z0.ZIndex.Value = 0;
        scene.Children.Add(z0);
        scene.Layers.Add(CreateLayer(0, audioMuted: true));
        using var compositor = new SceneCompositor(scene);

        CompositionFrame frame = compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5));

        Assert.That(frame.Objects.Length, Is.EqualTo(1));
        Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(z0.Objects[0]));
        Assert.That(frame.Eligibility, Is.Null,
            "Graphics frames do not consume eligibility and must not allocate a scene-wide snapshot.");
    }

    [Test]
    public void EvaluateGraphics_LockedElement_StillEvaluated()
    {
        Scene scene = CreateScene(_basePath);
        Element element = CreateElement(_basePath, isEnabled: true, new TestGraphicsObject());
        element.IsLocked.Value = true;
        scene.Children.Add(element);
        using var compositor = new SceneCompositor(scene);

        CompositionFrame frame = compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5));

        Assert.That(frame.Objects.Length, Is.EqualTo(1));
        Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(element.Objects[0]));
    }

    [Test]
    public void EvaluateGraphics_SoloToggledBetweenEvaluations_IsReflected()
    {
        Scene scene = CreateScene(_basePath);
        Element z0 = CreateElement(_basePath, isEnabled: true, new TestGraphicsObject());
        z0.ZIndex.Value = 0;
        Element z1 = CreateElement(_basePath, isEnabled: true, new TestGraphicsObject());
        z1.ZIndex.Value = 1;
        scene.Children.Add(z0);
        scene.Children.Add(z1);
        TimelineLayer layer1 = CreateLayer(1);
        scene.Layers.Add(CreateLayer(0));
        scene.Layers.Add(layer1);
        using var compositor = new SceneCompositor(scene);

        Assert.That(compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5)).Objects.Length, Is.EqualTo(2));

        layer1.IsSolo.Value = true;
        CompositionFrame frame = compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5));

        Assert.That(frame.Objects.Length, Is.EqualTo(1));
        Assert.That(frame.Objects[0].GetOriginal(), Is.SameAs(z1.Objects[0]));
    }

    [Test]
    public void EvaluateGraphics_MuteToggledBetweenEvaluations_IsReflected()
    {
        Scene scene = CreateScene(_basePath);
        Element element = CreateElement(_basePath, isEnabled: true, new TestGraphicsObject());
        scene.Children.Add(element);
        TimelineLayer layer = CreateLayer(0);
        scene.Layers.Add(layer);
        using var compositor = new SceneCompositor(scene);

        Assert.That(compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5)).Objects.Length, Is.EqualTo(1));

        layer.IsVideoMuted.Value = true;

        Assert.That(compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5)).Objects, Is.Empty);
    }

    [Test]
    public void EvaluateGraphics_LayerAddedBetweenEvaluations_IsReflected()
    {
        Scene scene = CreateScene(_basePath);
        Element element = CreateElement(_basePath, isEnabled: true, new TestGraphicsObject());
        scene.Children.Add(element);
        using var compositor = new SceneCompositor(scene);

        Assert.That(compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5)).Objects.Length, Is.EqualTo(1));

        scene.Layers.Add(CreateLayer(0, videoMuted: true));

        Assert.That(compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5)).Objects, Is.Empty);
    }

    [Test]
    public void EvaluateGraphics_LayerZIndexChangedBetweenEvaluations_IsReflected()
    {
        Scene scene = CreateScene(_basePath);
        Element element = CreateElement(_basePath, isEnabled: true, new TestGraphicsObject());
        element.ZIndex.Value = 1;
        scene.Children.Add(element);
        TimelineLayer layer = CreateLayer(0, videoMuted: true);
        scene.Layers.Add(layer);
        using var compositor = new SceneCompositor(scene);

        Assert.That(compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5)).Objects.Length, Is.EqualTo(1));

        layer.ZIndex.Value = 1;

        Assert.That(compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5)).Objects, Is.Empty);
    }

    [Test]
    public void EvaluateGraphics_CompositorForceOriginalTrue_SeedsPreferProxyFalse()
    {
        Scene scene = CreateScene(_basePath);
        var capture = new SceneCompositorContextCaptureDrawable();
        scene.Children.Add(CreateElement(_basePath, isEnabled: true, capture));
        using var compositor = new SceneCompositor(scene) { ForceOriginalSource = true };

        compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5));

        Assert.Multiple(() =>
        {
            Assert.That(capture.CapturedPreferProxy, Has.Count.EqualTo(1));
            Assert.That(capture.CapturedPreferProxy[0], Is.False);
        });
    }

    [TestCase(PreviewSourceMode.PreferProxy, true)]
    [TestCase(PreviewSourceMode.ForceOriginal, false)]
    public void EvaluateGraphics_CompositorForceOriginalFalse_SeedsPreferProxyFromGlobalConfig(
        PreviewSourceMode mode, bool expectedPreferProxy)
    {
        PreviewSourceMode original = GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.Value;
        try
        {
            GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.Value = mode;
            Scene scene = CreateScene(_basePath);
            var capture = new SceneCompositorContextCaptureDrawable();
            scene.Children.Add(CreateElement(_basePath, isEnabled: true, capture));
            using var compositor = new SceneCompositor(scene);

            compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5));

            Assert.Multiple(() =>
            {
                Assert.That(capture.CapturedPreferProxy, Has.Count.EqualTo(1));
                Assert.That(capture.CapturedPreferProxy[0], Is.EqualTo(expectedPreferProxy));
            });
        }
        finally
        {
            GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.Value = original;
        }
    }


    // The dangerous export configuration: the compositor forces original decode while the global
    // preview mode prefers proxies. Without SceneDrawable's propagation the nested compositor
    // would seed PreferProxy from the global config and export a referenced scene from proxies.
    [Test]
    public void EvaluateGraphics_ExportForceOriginal_OverridesPreferProxyConfigInReferencedScene()
    {
        PreviewSourceMode original = GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.Value;
        try
        {
            GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.Value = PreviewSourceMode.PreferProxy;
            Scene childScene = CreateScene(_basePath);
            var capture = new SceneCompositorContextCaptureDrawable();
            childScene.Children.Add(CreateElement(_basePath, isEnabled: true, capture));
            Scene parentScene = CreateScene(_basePath);
            var sceneDrawable = new SceneDrawable();
            sceneDrawable.ReferencedScene.Value = childScene;
            parentScene.Children.Add(CreateElement(_basePath, isEnabled: true, sceneDrawable));
            using var compositor = new SceneCompositor(parentScene) { ForceOriginalSource = true };

            compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5));

            Assert.Multiple(() =>
            {
                Assert.That(capture.CapturedPreferProxy, Has.Count.EqualTo(1));
                Assert.That(capture.CapturedPreferProxy[0], Is.False);
            });
        }
        finally
        {
            GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.Value = original;
        }
    }

    [Test]
    public void EvaluateAudio_ExportForceOriginal_PropagatesIntoReferencedSceneSound()
    {
        PreviewSourceMode original = GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.Value;
        try
        {
            GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.Value = PreviewSourceMode.PreferProxy;
            Scene childScene = CreateScene(_basePath);
            Scene parentScene = CreateScene(_basePath);
            var sceneSound = new SceneSound();
            sceneSound.ReferencedScene.Value = childScene;
            parentScene.Children.Add(CreateElement(_basePath, isEnabled: true, sceneSound));
            using var compositor = new SceneCompositor(parentScene) { ForceOriginalSource = true };

            CompositionFrame frame = compositor.EvaluateAudio(
                new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1)));

            var resource = (SceneSound.Resource)frame.Objects[0];
            Assert.Multiple(() =>
            {
                Assert.That(resource._compositor, Is.Not.Null);
                Assert.That(resource._compositor!.ForceOriginalSource, Is.True);
            });
        }
        finally
        {
            GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.Value = original;
        }
    }

    [Test]
    public void SceneSound_LatencyQueriesValidateSampleRateBeforeNestedComposerExists()
    {
        Scene childScene = CreateScene(_basePath);
        var sceneSound = new SceneSound();
        sceneSound.ReferencedScene.Value = childScene;
        sceneSound.TimeRange.Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1));
        using var resource = (SceneSound.Resource)sceneSound.ToResource(CompositionContext.Default);
        using var context = new AudioContext(48000, 2);

        sceneSound.Compose(context, resource);
        AudioNode nestedNode = context.GetOutputNodes().Single();
        while (nestedNode.Inputs.Count == 1)
            nestedNode = nestedNode.Inputs[0];

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => nestedNode.GetTotalLatencySamples(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => nestedNode.GetDrainLatencySamples(-1));
        });
    }

    [Test]
    public void EvaluateAudio_ReferencedSceneFlushesNestedLimiterTail()
    {
        const int sampleRate = 48000;
        const int clipSamples = 4096;
        const int lookaheadSamples = 240;
        var duration = MediaTime.FromSeconds((double)clipSamples / sampleRate);
        Scene childScene = CreateScene(_basePath);
        var childSound = new LimiterTailSound { LookaheadMs = 5f };
        Element childElement = CreateElement(_basePath, isEnabled: true, childSound);
        childElement.Length.Value = duration;
        childScene.Children.Add(childElement);

        Scene parentScene = CreateScene(_basePath);
        var sceneSound = new SceneSound();
        sceneSound.ReferencedScene.Value = childScene;
        Element parentElement = CreateElement(_basePath, isEnabled: true, sceneSound);
        parentElement.Length.Value = duration;
        parentScene.Children.Add(parentElement);

        using var compositor = new SceneCompositor(parentScene);
        using var composer = new Composer { SampleRate = sampleRate };

        var firstRange = new TimeRange(MediaTime.Zero, duration);
        using AudioBuffer? first = composer.Compose(firstRange, compositor.EvaluateAudio(firstRange));

        var tailRange = new TimeRange(
            duration,
            MediaTime.FromSeconds((double)lookaheadSamples / sampleRate));
        using AudioBuffer? tail = composer.Compose(tailRange, compositor.EvaluateAudio(tailRange));

        Assert.That(first, Is.Not.Null);
        Assert.That(tail, Is.Not.Null);
        Assert.That(
            tail!.GetChannelData(0)[..lookaheadSamples].ToArray(),
            Has.Some.Not.EqualTo(0f),
            "A referenced scene must propagate its nested limiter tail through SceneNode.Flush.");
    }

    [Test]
    public void EvaluateAudio_ReferencedSceneMuteSuppressesNestedLimiterTail()
    {
        const int sampleRate = 48000;
        const int clipSamples = 4096;
        const int lookaheadSamples = 240;
        var duration = MediaTime.FromSeconds((double)clipSamples / sampleRate);
        Scene childScene = CreateScene(_basePath);
        var childSound = new LimiterTailSound { LookaheadMs = 5f };
        Element childElement = CreateElement(_basePath, isEnabled: true, childSound);
        childElement.Length.Value = duration;
        childScene.Children.Add(childElement);
        TimelineLayer childLayer = CreateLayer(0);
        childScene.Layers.Add(childLayer);

        Scene parentScene = CreateScene(_basePath);
        var sceneSound = new SceneSound();
        sceneSound.ReferencedScene.Value = childScene;
        Element parentElement = CreateElement(_basePath, isEnabled: true, sceneSound);
        parentElement.Length.Value = duration;
        parentScene.Children.Add(parentElement);

        using var compositor = new SceneCompositor(parentScene);
        using var composer = new Composer { SampleRate = sampleRate };

        var firstRange = new TimeRange(MediaTime.Zero, duration);
        using AudioBuffer? first = composer.Compose(firstRange, compositor.EvaluateAudio(firstRange));

        childLayer.IsAudioMuted.Value = true;
        var tailRange = new TimeRange(
            duration,
            MediaTime.FromSeconds((double)lookaheadSamples / sampleRate));
        using AudioBuffer? tail = composer.Compose(tailRange, compositor.EvaluateAudio(tailRange));

        Assert.That(first, Is.Not.Null);
        Assert.That(tail, Is.Not.Null);
        Assert.That(
            tail!.GetChannelData(0)[..lookaheadSamples].ToArray(),
            Has.All.EqualTo(0f),
            "A referenced scene muted before the terminal drain must not emit its retained limiter tail.");
    }

    [Test]
    public void EvaluateAudio_TrimmedReferencedSceneUsesTerminalNestedDrainLatency()
    {
        const int sampleRate = 48000;
        const int childSamples = sampleRate;
        const int parentSamples = sampleRate / 2;
        var childDuration = AudioProcessContext.GetDurationForSampleCount(childSamples, sampleRate);
        var parentDuration = AudioProcessContext.GetDurationForSampleCount(parentSamples, sampleRate);
        Scene childScene = CreateScene(_basePath);
        var childSound = new LimiterDelayTailSound
        {
            LookaheadMs = 20f,
            LookaheadRampEnd = MediaTime.FromSeconds(0.5),
        };
        Element childElement = CreateElement(_basePath, isEnabled: true, childSound);
        childElement.Length.Value = childDuration;
        childScene.Children.Add(childElement);

        Scene parentScene = CreateScene(_basePath);
        var sceneSound = new SceneSound();
        sceneSound.ReferencedScene.Value = childScene;
        Element parentElement = CreateElement(_basePath, isEnabled: true, sceneSound);
        parentElement.Length.Value = parentDuration;
        parentScene.Children.Add(parentElement);

        using var compositor = new SceneCompositor(parentScene);
        using var composer = new Composer { SampleRate = sampleRate };

        var firstRange = new TimeRange(MediaTime.Zero, parentDuration);
        using AudioBuffer? first = composer.Compose(firstRange, compositor.EvaluateAudio(firstRange));

        var tailRange = new TimeRange(parentDuration, AudioProcessContext.GetDurationForSampleCount(960, sampleRate));
        using AudioBuffer? tail = composer.Compose(tailRange, compositor.EvaluateAudio(tailRange));

        Assert.That(first, Is.Not.Null);
        Assert.That(tail, Is.Not.Null);
        Assert.That(tail!.GetChannelData(0).ToArray(), Has.All.EqualTo(0f),
            "A trimmed referenced scene whose limiter is at terminal zero latency must not flush excess zero-fed stateful delay blocks.");
    }

    [Beutl.Engine.SuppressResourceClassGeneration]
    private class TestGraphicsObject : EngineObject
    {
        public override CompositionTarget GetCompositionTarget() => CompositionTarget.Graphics;
    }

    [Beutl.Engine.SuppressResourceClassGeneration]
    private sealed class DisposalCallbackObject(Action onDispose) : TestGraphicsObject
    {
        public override Resource ToResource(CompositionContext context)
        {
            var resource = new CallbackResource(onDispose);
            bool versionBumped = true;
            resource.Reconcile(this, context, ref versionBumped);
            return resource;
        }

        private sealed class CallbackResource(Action callback) : Resource
        {
            protected override void Dispose(bool disposing)
            {
                if (disposing) callback();
                base.Dispose(disposing);
            }
        }
    }

    [Beutl.Engine.SuppressResourceClassGeneration]
    private class TestAudioObject : EngineObject
    {
        public override CompositionTarget GetCompositionTarget() => CompositionTarget.Audio;
    }

    [Beutl.Engine.SuppressResourceClassGeneration]
    private sealed class ValueEqualEligibilityObject : EngineObject
    {
        public override bool Equals(object? obj) => obj is ValueEqualEligibilityObject;

        public override int GetHashCode() => 0;
    }

    private sealed class ValueEqualElement : Element
    {
        public override bool Equals(object? obj) => obj is ValueEqualElement;

        public override int GetHashCode() => 0;
    }
}

internal sealed partial class CountingAudioObject : EngineObject, IFlowOperator
{
    public int UpdateCount { get; private set; }

    public override CompositionTarget GetCompositionTarget() => CompositionTarget.Audio;

    public partial class Resource
    {
        partial void PostReconcile(CountingAudioObject obj, CompositionContext context)
        {
            obj.UpdateCount++;
        }
    }
}

internal sealed partial class SceneCompositorContextCaptureDrawable : Drawable
{
    public List<bool> CapturedPreferProxy { get; } = [];

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => Size.Empty;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    public partial class Resource
    {
        partial void PostReconcile(SceneCompositorContextCaptureDrawable obj, CompositionContext context)
        {
            obj.CapturedPreferProxy.Add(context.PreferProxy);
        }
    }
}
