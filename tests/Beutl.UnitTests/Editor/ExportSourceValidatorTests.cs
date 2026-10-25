using Beutl.Animation;
using Beutl.Audio;
using Beutl.Editor;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public sealed class ExportSourceValidatorTests
{
    private static readonly TimeRange s_wholeScene = new(MediaTime.Zero, MediaTime.FromSeconds(10));

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void GetMissingPaths_ReturnsMissingSourcesReferencedByScene()
    {
        string existingPath = Path.Combine(_root, "existing.mov");
        string missingPath = Path.Combine(_root, "missing.mov");
        File.WriteAllBytes(existingPath, [1]);

        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(_root, "test.scene")),
        };
        scene.Children.Add(CreateVideoElement(_root, existingPath));
        scene.Children.Add(CreateVideoElement(_root, missingPath));

        IReadOnlySet<string> referenced = ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene);
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(referenced);

        Assert.That(missing, Is.EqualTo(new[] { missingPath }));
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public void CollectRenderableSources_OnlyChecksEnabledExportPasses(bool video, bool audio)
    {
        string missingVideo = Path.Combine(_root, "missing.mov");
        string missingSound = Path.Combine(_root, "missing.wav");
        var scene = new Scene(1920, 1080, string.Empty);
        scene.Children.Add(CreateVideoElement(_root, missingVideo));
        scene.Children.Add(CreateSoundElement(_root, missingSound));

        var paths = ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene, video, audio);

        Assert.Multiple(() =>
        {
            Assert.That(paths.Contains(missingVideo), Is.EqualTo(video));
            Assert.That(paths.Contains(missingSound), Is.EqualTo(audio));
        });
    }

    // A SceneDrawable renders only the referenced scene's graphics (SceneDrawable.EvaluateGraphics), so
    // a missing image inside it blocks export, but an audio-only Sound the video render never opens must
    // not — even for a full export. A SceneSound (below) is what pulls that scene's audio.
    [Test]
    public void GetMissingPaths_GraphicallyEmbeddedScene_ReportsImageButNotAudioOnlySound()
    {
        string missingImage = Path.Combine(_root, "missing.png");
        string missingSound = Path.Combine(_root, "missing.mp3");

        var childScene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(_root, "child.scene")),
        };
        childScene.Children.Add(CreateImageElement(_root, missingImage));
        childScene.Children.Add(CreateSoundElement(_root, missingSound));

        var sceneDrawable = new SceneDrawable();
        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(_root, "root.scene")),
        };
        sceneDrawable.ReferencedScene.Expression = SceneReferenceTestHelper.Create(scene, childScene);
        scene.Children.Add(ElementWith(_root, sceneDrawable));

        IReadOnlySet<string> referenced = ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene);
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(referenced);

        Assert.That(missing, Is.EqualTo(new[] { missingImage }));
    }

    // An embed renders the referenced scene from its own window start, so a clip before that window is
    // never composed and its missing file must not block export.
    [Test]
    public void CollectRenderableSources_GraphicallyEmbeddedSceneWithStart_ExcludesPreWindowClips()
    {
        string beforeWindow = Path.Combine(_root, "before.png");
        string insideWindow = Path.Combine(_root, "inside.png");

        var childScene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(_root, "child.scene")),
            Start = { Value = MediaTime.FromSeconds(5) },
        };
        childScene.Children.Add(CreateImageElement(_root, beforeWindow));
        Element inside = CreateImageElement(_root, insideWindow);
        inside.Start.Value = MediaTime.FromSeconds(5);
        childScene.Children.Add(inside);

        var sceneDrawable = new SceneDrawable();
        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(_root, "root.scene")),
        };
        sceneDrawable.ReferencedScene.Expression = SceneReferenceTestHelper.Create(scene, childScene);
        scene.Children.Add(ElementWith(_root, sceneDrawable, length: MediaTime.FromSeconds(10)));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Is.EqualTo(new[] { insideWindow }));
    }

    // A SceneSound renders only the referenced scene's audio (SceneSound.EvaluateAudio), so its missing
    // sound blocks export while the scene's graphics-only image, which this embed never renders, does not.
    [Test]
    public void GetMissingPaths_AudiallyEmbeddedScene_ReportsSoundButNotGraphicsOnlyImage()
    {
        string missingImage = Path.Combine(_root, "missing.png");
        string missingSound = Path.Combine(_root, "missing.mp3");

        var childScene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(_root, "child.scene")),
        };
        childScene.Children.Add(CreateImageElement(_root, missingImage));
        childScene.Children.Add(CreateSoundElement(_root, missingSound));

        var sceneSound = new SceneSound();
        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(_root, "root.scene")),
        };
        sceneSound.ReferencedScene.Expression = SceneReferenceTestHelper.Create(scene, childScene);
        scene.Children.Add(ElementWith(_root, sceneSound));

        IReadOnlySet<string> referenced = ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene);
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(referenced);

        Assert.That(missing, Is.EqualTo(new[] { missingSound }));
    }

    // Same as the graphics case: an audio embed composes the referenced scene from its own window
    // start, so a sound clip before it must not be preflighted.
    [Test]
    public void CollectRenderableSources_AudiallyEmbeddedSceneWithStart_ExcludesPreWindowClips()
    {
        string beforeWindow = Path.Combine(_root, "before.mp3");
        string insideWindow = Path.Combine(_root, "inside.mp3");

        var childScene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(_root, "child.scene")),
            Start = { Value = MediaTime.FromSeconds(5) },
        };
        childScene.Children.Add(CreateSoundElement(_root, beforeWindow));
        Element inside = CreateSoundElement(_root, insideWindow);
        inside.Start.Value = MediaTime.FromSeconds(5);
        childScene.Children.Add(inside);

        var sceneSound = new SceneSound();
        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(_root, "root.scene")),
        };
        sceneSound.ReferencedScene.Expression = SceneReferenceTestHelper.Create(scene, childScene);
        scene.Children.Add(ElementWith(_root, sceneSound, length: MediaTime.FromSeconds(10)));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Is.EqualTo(new[] { insideWindow }));
    }

    // A disabled element never renders (SceneCompositor.SortLayers gates on IsEnabled), so its missing
    // original must not block export of the rest of the scene.
    [Test]
    public void CollectRenderableSources_SkipsDisabledElement()
    {
        string missingPath = Path.Combine(_root, "missing.mov");

        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(_root, "test.scene")),
        };
        Element disabled = CreateVideoElement(_root, missingPath);
        disabled.IsEnabled.Value = false;
        scene.Children.Add(disabled);

        IReadOnlySet<string> referenced = ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene);
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(referenced);

        Assert.That(missing, Is.Empty);
    }

    // An element whose time range does not intersect the exported range never renders, so its missing
    // original must not block export.
    [Test]
    public void CollectRenderableSources_SkipsOutOfRangeElement()
    {
        string missingPath = Path.Combine(_root, "missing.mov");

        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(_root, "test.scene")),
        };
        Element outOfRange = CreateVideoElement(_root, missingPath);
        outOfRange.Start.Value = MediaTime.FromSeconds(20);
        scene.Children.Add(outOfRange);

        IReadOnlySet<string> referenced = ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene);
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(referenced);

        Assert.That(missing, Is.Empty);
    }

    // The single-frame overload includes only elements whose range contains the frame time — the
    // save-frame preflight must not block on sources that are absent at the current frame.
    [Test]
    public void CollectRenderableSources_SingleFrame_OnlyIncludesElementsAtThatTime()
    {
        string atFrame = Path.Combine(_root, "at-frame.mov");
        string laterOnly = Path.Combine(_root, "later-only.mov");

        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(_root, "test.scene")),
        };
        Element visible = CreateVideoElement(_root, atFrame);
        visible.Start.Value = MediaTime.Zero;
        visible.Length.Value = MediaTime.FromSeconds(2);
        scene.Children.Add(visible);

        Element later = CreateVideoElement(_root, laterOnly);
        later.Start.Value = MediaTime.FromSeconds(5);
        later.Length.Value = MediaTime.FromSeconds(2);
        scene.Children.Add(later);

        IReadOnlySet<string> referenced =
            ExportSourceValidator.CollectRenderableSources(scene, MediaTime.FromSeconds(1));

        Assert.That(referenced, Is.EquivalentTo(new[] { atFrame }));
    }

    // A disabled clip inside a referenced scene never renders (SceneCompositor.SortLayers gates
    // on IsEnabled within the referenced scene too), so its missing original must not block export while
    // an enabled sibling's missing original still does.
    [Test]
    public void CollectRenderableSources_SkipsDisabledElementInReferencedScene()
    {
        string missingEnabled = Path.Combine(_root, "enabled.png");
        string missingDisabled = Path.Combine(_root, "disabled.png");

        var childScene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(_root, "child.scene")),
        };
        childScene.Children.Add(CreateImageElement(_root, missingEnabled));
        Element disabled = CreateImageElement(_root, missingDisabled);
        disabled.IsEnabled.Value = false;
        childScene.Children.Add(disabled);

        var sceneDrawable = new SceneDrawable();
        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(_root, "root.scene")),
        };
        sceneDrawable.ReferencedScene.Expression = SceneReferenceTestHelper.Create(scene, childScene);
        scene.Children.Add(ElementWith(_root, sceneDrawable));

        IReadOnlySet<string> referenced = ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene);
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(referenced);

        Assert.That(missing, Is.EqualTo(new[] { missingEnabled }));
    }

    // An enabled element can hold a disabled object; the render path (Element.CollectObjects)
    // skips disabled objects before opening media, so a missing file on a disabled object must not
    // block export even though its owning element renders.
    [Test]
    public void CollectRenderableSources_SkipsDisabledObjectInEnabledElement()
    {
        string enabledMissing = Path.Combine(_root, "enabled.mov");
        string disabledMissing = Path.Combine(_root, "disabled.mov");

        SourceVideo enabled = VideoDrawable(enabledMissing);
        SourceVideo disabled = VideoDrawable(disabledMissing);
        disabled.IsEnabled.Value = false;
        Element element = ElementWith(_root, enabled);
        element.AddObject(disabled);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(element);

        IReadOnlySet<string> referenced = ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene);
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(referenced);

        Assert.That(missing, Is.EqualTo(new[] { enabledMissing }));
    }

    // Save-frame renders graphics only (EvaluateGraphics filters CompositionTarget.Audio via
    // Element.CollectObjects), so a missing audio original must not block a still image; a full export
    // (which composes audio) still requires it.
    [Test]
    public void CollectRenderableSources_SingleFrame_ExcludesAudioSourcesButExportIncludesThem()
    {
        string missingVideo = Path.Combine(_root, "missing.mov");
        string missingSound = Path.Combine(_root, "missing.mp3");

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(CreateVideoElement(_root, missingVideo));
        scene.Children.Add(CreateSoundElement(_root, missingSound));

        IReadOnlyList<string> frame = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, MediaTime.Zero));
        IReadOnlyList<string> export = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.Multiple(() =>
        {
            Assert.That(frame, Is.EqualTo(new[] { missingVideo }));
            Assert.That(export, Is.EquivalentTo(new[] { missingVideo, missingSound }));
        });
    }

    // Export renders [Scene.Start, Scene.Start + Duration); a preflight range that starts at
    // Scene.Start includes clips in the exported segment and excludes ones entirely before it.
    [Test]
    public void CollectRenderableSources_RangeStartingAtSceneStart_TracksExportedSegment()
    {
        string beforeStart = Path.Combine(_root, "before.mov");
        string inSegment = Path.Combine(_root, "in-segment.mov");

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        Element before = CreateVideoElement(_root, beforeStart);
        before.Start.Value = MediaTime.Zero;
        before.Length.Value = MediaTime.FromSeconds(2);
        scene.Children.Add(before);
        Element inside = CreateVideoElement(_root, inSegment);
        inside.Start.Value = MediaTime.FromSeconds(5);
        inside.Length.Value = MediaTime.FromSeconds(2);
        scene.Children.Add(inside);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(
                scene, new TimeRange(MediaTime.FromSeconds(5), MediaTime.FromSeconds(5))));

        Assert.That(missing, Is.EqualTo(new[] { inSegment }));
    }

    // An animated Source keyframe is filtered to the render window. For an object value the animator
    // samples the NEXT keyframe's value, so the middle key (missing) governs (0s, 5s]: a window inside
    // that span requires it, while a window entirely after it (sampling the later key) must not.
    [Test]
    public void CollectRenderableSources_FiltersAnimatedSourceKeyframesToRenderWindow()
    {
        string startExisting = Path.Combine(_root, "start.mov");
        string midMissing = Path.Combine(_root, "mid.mov");
        string endExisting = Path.Combine(_root, "end.mov");
        File.WriteAllBytes(startExisting, [1]);
        File.WriteAllBytes(endExisting, [1]);

        var drawable = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = MakeVideoSource(startExisting) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(5) }, Value = { Value = MakeVideoSource(midMissing) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(10) }, Value = { Value = MakeVideoSource(endExisting) } });
        drawable.Source.SetKeyFrameAnimation(animation);

        Element element = ElementWith(_root, drawable, length: MediaTime.FromSeconds(15));
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(element);

        // [0s, 3s) is inside the mid key's [0s, 5s) span → the missing mid file is required.
        IReadOnlyList<string> inMidSpan = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(3))));
        // [6s, 10s) samples only the end key → the mid file is not required.
        IReadOnlyList<string> afterMid = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(MediaTime.FromSeconds(6), MediaTime.FromSeconds(4))));

        Assert.Multiple(() =>
        {
            Assert.That(inMidSpan, Is.EqualTo(new[] { midMissing }));
            Assert.That(afterMid, Is.Empty);
        });
    }

    // The visited-scene set is keyed by (scene, target): the same scene embedded once as a
    // SceneDrawable (Graphics) and once as a SceneSound (Audio) must have BOTH facets preflighted, so
    // a Graphics visit must not suppress the Audio one (or vice versa).
    [Test]
    public void GetMissingPaths_SameSceneAsDrawableAndSound_ReportsBothFacets()
    {
        string missingImage = Path.Combine(_root, "missing.png");
        string missingSound = Path.Combine(_root, "missing.mp3");

        var childScene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "child.scene")) };
        childScene.Children.Add(CreateImageElement(_root, missingImage));
        childScene.Children.Add(CreateSoundElement(_root, missingSound));

        var sceneDrawable = new SceneDrawable();
        var sceneSound = new SceneSound();
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "root.scene")) };
        sceneSound.ReferencedScene.Expression = SceneReferenceTestHelper.Create(scene, childScene);
        sceneDrawable.ReferencedScene.Expression = SceneReferenceTestHelper.Create(scene, childScene);
        scene.Children.Add(ElementWith(_root, sceneDrawable));
        scene.Children.Add(ElementWith(_root, sceneSound));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Is.EquivalentTo(new[] { missingImage, missingSound }));
    }

    // A SoundGroup renders its child Sounds through SoundGroup.Compose (the audio analogue of a
    // DrawableGroup), so a missing original nested in one must be preflighted, not surface at encode.
    [Test]
    public void GetMissingPaths_ReportsMissingSoundNestedInSoundGroup()
    {
        string missingSound = Path.Combine(_root, "missing.mp3");

        var group = new SoundGroup();
        group.Children.Add(SoundDrawable(missingSound));
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(ElementWith(_root, group));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Is.EqualTo(new[] { missingSound }));
    }

    // Global-clock keyframes use scene time, not element-local time, so a source key sampled inside the
    // exported interval must not be dropped by subtracting the element start; it must still block export.
    [Test]
    public void CollectRenderableSources_GlobalClockAnimatedSource_NotDroppedByLocalWindow()
    {
        string existing = Path.Combine(_root, "existing.mov");
        string missingAtGlobal12 = Path.Combine(_root, "missing.mov");
        File.WriteAllBytes(existing, [1]);

        var drawable = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?> { UseGlobalClock = { Value = true } };
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(11) }, Value = { Value = MakeVideoSource(existing) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(12) }, Value = { Value = MakeVideoSource(missingAtGlobal12) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(13) }, Value = { Value = MakeVideoSource(existing) } });
        drawable.Source.SetKeyFrameAnimation(animation);

        Element element = ElementWith(_root, drawable, MediaTime.FromSeconds(10), MediaTime.FromSeconds(10));
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(element);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(MediaTime.FromSeconds(10), MediaTime.FromSeconds(10))));

        Assert.That(missing, Is.EqualTo(new[] { missingAtGlobal12 }));
    }

    // When a property is animated by keyframes, the render samples the animation, not the base
    // Value, so a stale missing base must not block export while the keyframed values exist.
    [Test]
    public void CollectRenderableSources_AnimatedProperty_DoesNotReportOverriddenBaseValue()
    {
        string missingBase = Path.Combine(_root, "old-base.mov");
        string keyframed = Path.Combine(_root, "keyframed.mov");
        File.WriteAllBytes(keyframed, [1]);

        var drawable = new SourceVideo();
        drawable.Source.Value = MakeVideoSource(missingBase);
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = MakeVideoSource(keyframed) } });
        drawable.Source.SetKeyFrameAnimation(animation);
        Element element = ElementWith(_root, drawable);
        element.Length.Value = MediaTime.FromSeconds(10);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(element);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(10))));

        Assert.That(missing, Is.Empty);
    }

    // The outgoing key governs the left-open span (-inf, 0s] and the incoming key takes over on (0s, +inf),
    // so an export window at [5s, 10s) samples only the incoming key and must not report the outgoing file.
    [Test]
    public void CollectRenderableSources_SourceSwitchExactlyAtWindowStart_DropsOutgoingKeyframe()
    {
        string outgoingMissing = Path.Combine(_root, "outgoing.mov");
        string incoming = Path.Combine(_root, "incoming.mov");
        File.WriteAllBytes(incoming, [1]);

        var drawable = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = MakeVideoSource(outgoingMissing) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(5) }, Value = { Value = MakeVideoSource(incoming) } });
        drawable.Source.SetKeyFrameAnimation(animation);
        Element element = ElementWith(_root, drawable);
        element.Length.Value = MediaTime.FromSeconds(10);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(element);

        // Export [5s, 10s): the outgoing key governs (-inf, 0s] and is not sampled, so it must be dropped.
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(MediaTime.FromSeconds(5), MediaTime.FromSeconds(5))));

        Assert.That(missing, Is.Empty);
    }

    // A DrawableDecorator renders its children at the same composition time, so the render window still
    // maps directly — an out-of-window animated keyframe under a decorator must be dropped, not reported.
    [Test]
    public void CollectRenderableSources_AnimatedSourceInDecorator_FiltersToRenderWindow()
    {
        string oldMissing = Path.Combine(_root, "old.mov");
        string newExisting = Path.Combine(_root, "new.mov");
        File.WriteAllBytes(newExisting, [1]);

        var child = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = MakeVideoSource(oldMissing) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(5) }, Value = { Value = MakeVideoSource(newExisting) } });
        child.Source.SetKeyFrameAnimation(animation);
        var decorator = new DrawableDecorator();
        decorator.Children.Add(child);
        Element element = ElementWith(_root, decorator);
        element.Length.Value = MediaTime.FromSeconds(10);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(element);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(MediaTime.FromSeconds(6), MediaTime.FromSeconds(4))));

        Assert.That(missing, Is.Empty);
    }

    // Node-graph input-port animations must honour the render window too: an out-of-window keyframe on
    // a VideoSourceNode input must be dropped, so a since-replaced file it references cannot block export.
    [Test]
    public void CollectRenderableSources_AnimatedNodeGraphInput_FiltersToRenderWindow()
    {
        string oldMissing = Path.Combine(_root, "old.mov");
        string newExisting = Path.Combine(_root, "new.mov");
        File.WriteAllBytes(newExisting, [1]);

        var node = new VideoSourceNode();
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = MakeVideoSource(oldMissing) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(5) }, Value = { Value = MakeVideoSource(newExisting) } });
        ((IAnimatablePropertyAdapter<VideoSource?>)node.Source.Property!).Animation = animation;
        var drawable = new NodeGraphDrawable();
        drawable.Model.Value!.Nodes.Add(node);
        Element element = ElementWith(_root, drawable);
        element.Length.Value = MediaTime.FromSeconds(10);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(element);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(MediaTime.FromSeconds(6), MediaTime.FromSeconds(4))));

        Assert.That(missing, Is.Empty);
    }

    // A rendered source nested inside an EngineObject-valued property (a Shape's Fill ImageBrush holding
    // ImageBrush.Source) is opened at render, so a missing such file must be preflighted, not slip past.
    [Test]
    public void CollectRenderableSources_ReportsMissingSourceInsideBrushProperty()
    {
        string missingImage = Path.Combine(_root, "brush.png");

        var brush = new ImageBrush();
        var imageSource = new ImageSource();
        imageSource.ReadFrom(new Uri(missingImage));
        brush.Source.Value = imageSource;
        var shape = new RectShape();
        shape.Fill.Value = brush;
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(ElementWith(_root, shape));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Is.EqualTo(new[] { missingImage }));
    }

    // A single-frame preflight is a point sample, not an empty half-open window. GetPreviousAndNextKeyFrame
    // makes a key active on the left-open span (previous key time, this key time], so at t=5s the middle
    // key b is sampled (b's span is (0s, 5s]); the point sample must keep b, not treat it as an empty range.
    [Test]
    public void CollectRenderableSources_SingleFrameAtKeyframeTime_KeepsActiveKeyframe()
    {
        string a = Path.Combine(_root, "a.mov");
        string missingB = Path.Combine(_root, "b.mov");
        string c = Path.Combine(_root, "c.mov");
        File.WriteAllBytes(a, [1]);
        File.WriteAllBytes(c, [1]);

        var drawable = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = MakeVideoSource(a) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(5) }, Value = { Value = MakeVideoSource(missingB) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(10) }, Value = { Value = MakeVideoSource(c) } });
        drawable.Source.SetKeyFrameAnimation(animation);
        Element element = ElementWith(_root, drawable, length: MediaTime.FromSeconds(15));
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(element);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, MediaTime.FromSeconds(5)));

        Assert.That(missing, Is.EqualTo(new[] { missingB }));
    }

    // A null-valued keyframe makes GetAnimatedValue fall back to the base Value, so the base
    // must not be suppressed as overridden — its missing file still blocks export.
    [Test]
    public void CollectRenderableSources_AnimationWithNullKeyframe_KeepsBaseSource()
    {
        string missingBase = Path.Combine(_root, "base.mov");

        var drawable = new SourceVideo();
        drawable.Source.Value = MakeVideoSource(missingBase);
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = null } });
        drawable.Source.SetKeyFrameAnimation(animation);
        Element element = ElementWith(_root, drawable);
        element.Length.Value = MediaTime.FromSeconds(10);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(element);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(10))));

        Assert.That(missing, Is.EqualTo(new[] { missingBase }));
    }

    // The window handed to a SceneDrawable's referenced scene is already element-local (sceneWindow -
    // element.Start); subtracting sceneDrawable.Start again double-shifts it and drops in-window
    // keyframes for a non-zero-start element. The inner key at 2s sits inside the [0,10] local window
    // and must be reported — a double-subtraction would map the window to [-10,0] and drop it.
    [Test]
    public void CollectRenderableSources_NonZeroStartElement_ReferencedSceneWindowNotDoubleShifted()
    {
        string existing = Path.Combine(_root, "existing.mov");
        string missingAt2s = Path.Combine(_root, "at-2s.mov");
        File.WriteAllBytes(existing, [1]);

        var childDrawable = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = MakeVideoSource(existing) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(2) }, Value = { Value = MakeVideoSource(missingAt2s) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(8) }, Value = { Value = MakeVideoSource(existing) } });
        childDrawable.Source.SetKeyFrameAnimation(animation);
        Element childElement = ElementWith(_root, childDrawable, length: MediaTime.FromSeconds(10));
        var childScene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "child.scene")) };
        childScene.Children.Add(childElement);

        var sceneDrawable = new SceneDrawable();
        Element outerElement = ElementWith(_root, sceneDrawable, MediaTime.FromSeconds(10), MediaTime.FromSeconds(10));
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "root.scene")) };
        sceneDrawable.ReferencedScene.Expression = SceneReferenceTestHelper.Create(scene, childScene);
        scene.Children.Add(outerElement);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(MediaTime.FromSeconds(10), MediaTime.FromSeconds(10))));

        Assert.That(missing, Is.EqualTo(new[] { missingAt2s }));
    }

    // A global-clock keyframe is sampled at scene time. For an object value the animator returns the
    // NEXT key's value, so a key governs (previous key time, this key time]; the last one holds to +inf.
    // A key whose span is entirely outside the exported scene-time window must be dropped.
    [Test]
    public void CollectRenderableSources_GlobalClockKeyframeOutsideSceneWindow_IsDropped()
    {
        string existing = Path.Combine(_root, "existing.mov");
        string missingAtGlobal30 = Path.Combine(_root, "missing.mov");
        File.WriteAllBytes(existing, [1]);

        var drawable = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?> { UseGlobalClock = { Value = true } };
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(10) }, Value = { Value = MakeVideoSource(existing) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(20) }, Value = { Value = MakeVideoSource(existing) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(30) }, Value = { Value = MakeVideoSource(missingAtGlobal30) } });
        drawable.Source.SetKeyFrameAnimation(animation);

        Element element = ElementWith(_root, drawable, MediaTime.FromSeconds(5), MediaTime.FromSeconds(30));
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(element);

        // Export [10s,12s]: the 30s global key governs (20s, +inf) — no exported frame samples it, so its
        // missing file must not block export.
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(MediaTime.FromSeconds(10), MediaTime.FromSeconds(2))));

        Assert.That(missing, Is.Empty);
    }

    // Without clamping, an export window wider than the element maps to pre-Start (negative) local time
    // and keeps element-local keyframes never rendered. Element Start=10s, export [15s, 20s]: only local
    // [5s, 10s] renders. Unclamped, SubtractStart(10) on the scene window [0s, 20s] would give [-10s, 10s]
    // and pull in the local-0s key; clamping to the element's active interval first excludes it.
    [Test]
    public void CollectRenderableSources_LateStartingElement_WindowClampedToActiveInterval()
    {
        string existing = Path.Combine(_root, "existing.mov");
        string missingEarly = Path.Combine(_root, "early.mov");
        File.WriteAllBytes(existing, [1]);

        // Element starts at scene 10s. Local-0s key governs (-inf, 0s]; local-3s key holds from (0s, 3s];
        // local-12s key holds from (3s, +inf). Only the last two are sampled inside local [5s, 10s].
        var drawable = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = MakeVideoSource(missingEarly) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(3) }, Value = { Value = MakeVideoSource(existing) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(12) }, Value = { Value = MakeVideoSource(existing) } });
        drawable.Source.SetKeyFrameAnimation(animation);
        Element element = ElementWith(_root, drawable, MediaTime.FromSeconds(10), MediaTime.FromSeconds(15));
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(element);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(MediaTime.FromSeconds(15), MediaTime.FromSeconds(5))));

        Assert.That(missing, Is.Empty);
    }

    // When a property carries an expression, IExpressionProperty.Evaluate evaluates the expression and never
    // samples the base or the animation, so a missing base/keyframe source must not block export while
    // the expression resolves to an existing source.
    [Test]
    public void CollectRenderableSources_ExpressionOverridesBaseAndAnimation()
    {
        string missingBase = Path.Combine(_root, "base.mov");
        string missingKey = Path.Combine(_root, "key.mov");
        string exprTarget = Path.Combine(_root, "expr.mov");
        File.WriteAllBytes(exprTarget, [1]);

        var referenced = new SourceVideo();
        referenced.Source.Value = MakeVideoSource(exprTarget);

        var drawable = new SourceVideo();
        drawable.Source.Value = MakeVideoSource(missingBase);
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = MakeVideoSource(missingKey) } });
        drawable.Source.SetKeyFrameAnimation(animation);
        drawable.Source.Expression = Beutl.Engine.Expressions.Expression.CreateReference<VideoSource>(referenced.Id.Value, "Source");

        Element referencedElement = ElementWith(_root, referenced);
        Element element = ElementWith(_root, drawable);
        element.Length.Value = MediaTime.FromSeconds(10);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(referencedElement);
        scene.Children.Add(element);

        // Windowed export: the expression wins, so the missing base/key must not block; only the resolved
        // (existing) expression target is opened.
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(10))));

        Assert.That(missing, Is.Empty);
    }

    // A node-graph input on the global clock is sampled at scene time, so a global-clock source key
    // outside the exported scene window must be dropped, not fall back to a broad walk that blocks export.
    [Test]
    public void CollectRenderableSources_GlobalClockNodeInput_FilteredBySceneWindow()
    {
        string existing = Path.Combine(_root, "existing.mov");
        string missingLate = Path.Combine(_root, "late.mov");
        File.WriteAllBytes(existing, [1]);

        var node = new VideoSourceNode();
        var animation = new KeyFrameAnimation<VideoSource?> { UseGlobalClock = { Value = true } };
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(10) }, Value = { Value = MakeVideoSource(existing) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(20) }, Value = { Value = MakeVideoSource(existing) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(30) }, Value = { Value = MakeVideoSource(missingLate) } });
        ((IAnimatablePropertyAdapter<VideoSource?>)node.Source.Property!).Animation = animation;
        var drawable = new NodeGraphDrawable();
        drawable.Model.Value!.Nodes.Add(node);
        Element element = ElementWith(_root, drawable, MediaTime.FromSeconds(5), MediaTime.FromSeconds(30));
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(element);

        // Export [10s, 12s]: the 30s global key governs scene (20s, +inf), never sampled, so its missing
        // file must not block export.
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(MediaTime.FromSeconds(10), MediaTime.FromSeconds(2))));

        Assert.That(missing, Is.Empty);
    }

    // A referenced-scene child whose range does not intersect the exported window never renders (SortLayers
    // gates on Range.Intersects), so its direct missing source must not block export.
    [Test]
    public void CollectRenderableSources_ReferencedSceneChildOutsideWindow_IsSkipped()
    {
        string missingImage = Path.Combine(_root, "out-of-window.png");

        var childScene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "child.scene")) };
        Element outOfWindow = CreateImageElement(_root, missingImage);
        outOfWindow.Start.Value = MediaTime.FromSeconds(30);
        outOfWindow.Length.Value = MediaTime.FromSeconds(5);
        childScene.Children.Add(outOfWindow);

        var sceneDrawable = new SceneDrawable();
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "root.scene")) };
        sceneDrawable.ReferencedScene.Expression = SceneReferenceTestHelper.Create(scene, childScene);
        Element outer = ElementWith(_root, sceneDrawable);
        outer.Length.Value = MediaTime.FromSeconds(10);
        scene.Children.Add(outer);

        // Export [0s, 5s]: the referenced-scene child at 30s never composes, so its missing image is dropped.
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(5))));

        Assert.That(missing, Is.Empty);
    }

    // A SceneSound with the identity audio map (OffsetPosition 0, Speed 100, unanimated) samples the
    // referenced scene in element-local time, so a referenced-scene child outside the window is dropped;
    // a non-identity map (animated Speed) falls back to the full walk and keeps it.
    [Test]
    public void CollectRenderableSources_IdentitySceneSound_WindowsReferencedAudio()
    {
        string missingLate = Path.Combine(_root, "late.mp3");

        Scene BuildRoot(bool animateSpeed)
        {
            var childScene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, $"child-{Guid.NewGuid():N}.scene")) };
            Element lateChild = CreateSoundElement(_root, missingLate);
            lateChild.Start.Value = MediaTime.FromSeconds(30);
            lateChild.Length.Value = MediaTime.FromSeconds(5);
            childScene.Children.Add(lateChild);

            var sceneSound = new SceneSound();
            if (animateSpeed)
            {
                var speed = new KeyFrameAnimation<float>();
                speed.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 100f } });
                speed.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(5) }, Value = { Value = 200f } });
                sceneSound.Speed.SetKeyFrameAnimation(speed);
            }

            var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, $"root-{Guid.NewGuid():N}.scene")) };
            sceneSound.ReferencedScene.Expression = SceneReferenceTestHelper.Create(scene, childScene);
            Element outer = ElementWith(_root, sceneSound);
            outer.Length.Value = MediaTime.FromSeconds(40);
            scene.Children.Add(outer);
            return scene;
        }

        // Export [0s, 5s]: identity map windows the referenced scene, so the 30s child never composes and
        // its missing file is dropped; the animated-Speed map is a real remap and conservatively keeps it.
        IReadOnlyList<string> identityMissing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(BuildRoot(animateSpeed: false), new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(5))));
        IReadOnlyList<string> remappedMissing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(BuildRoot(animateSpeed: true), new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(5))));

        Assert.Multiple(() =>
        {
            Assert.That(identityMissing, Is.Empty);
            Assert.That(remappedMissing, Does.Contain(missingLate));
        });
    }

    // A DrawablePresenter whose Target is supplied by a reference-expression renders the resolved target,
    // so a missing source inside it must be preflighted even though Target.Value is null.
    [Test]
    public void CollectRenderableSources_ExpressionSuppliedPresenterTarget_IsWalked()
    {
        string missingVideo = Path.Combine(_root, "presented.mov");

        var presented = VideoDrawable(missingVideo);
        var presenter = new DrawablePresenter();
        presenter.Target.Expression = Beutl.Engine.Expressions.Expression.CreateReference<Drawable>(presented.Id.Value);

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(ElementWith(_root, presented));
        scene.Children.Add(ElementWith(_root, presenter));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Does.Contain(missingVideo));
    }

    // One walk reaches the same target first via a window-preserving DrawablePresenter and
    // then via a time-remapping DrawableTimeController. The controller's rangeless full walk is a superset
    // of the windowed one, so an identity-only visited check that lets the presenter suppress the
    // controller would omit media reachable only at the remapped time. A [6s,10s] export windows out the
    // 5s keyframe on every windowed path (the target's own element and the presenter), leaving the
    // controller's full walk the only path that reaches the missing remapped source.
    [Test]
    public void CollectRenderableSources_TargetViaPresenterThenTimeController_KeepsRemappedMedia()
    {
        string existing = Path.Combine(_root, "existing.mov");
        string remappedMissing = Path.Combine(_root, "remapped.mov");
        File.WriteAllBytes(existing, [1]);

        var target = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = MakeVideoSource(existing) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(5) }, Value = { Value = MakeVideoSource(remappedMissing) } });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(10) }, Value = { Value = MakeVideoSource(existing) } });
        target.Source.SetKeyFrameAnimation(animation);

        // Both flow operators reference the same target instance (Target is a reference property, so
        // sharing is allowed) so one walk visits it twice via the shared visited set.
        var presenter = new DrawablePresenter();
        presenter.Target.Value = target;
        var controller = new DrawableTimeController();
        controller.Target.Value = target;
        // Presenter first: its windowed visit would suppress the controller under an identity-only check.
        var group = new DrawableGroup();
        group.Children.Add(presenter);
        group.Children.Add(controller);

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        Element groupElement = ElementWith(_root, group);
        groupElement.Length.Value = MediaTime.FromSeconds(15);
        scene.Children.Add(groupElement);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(MediaTime.FromSeconds(6), MediaTime.FromSeconds(4))));

        Assert.That(missing, Does.Contain(remappedMissing));
    }

    // When a presenter Target has both a Value and a reference-expression, the render evaluates
    // the expression, so preflight must walk the expression's target — not the (stale) Value.
    [Test]
    public void CollectRenderableSources_PresenterTargetExpression_TakesPrecedenceOverCurrentValue()
    {
        string currentVideo = Path.Combine(_root, "current.mov");
        string expressionVideo = Path.Combine(_root, "expression.mov");
        File.WriteAllBytes(currentVideo, [1]);

        // Value points at an existing file; the expression points at a missing one. The render
        // opens the expression target, so its missing file must be reported.
        var currentTarget = VideoDrawable(currentVideo);
        var expressionTarget = VideoDrawable(expressionVideo);
        var presenter = new DrawablePresenter();
        presenter.Target.Value = currentTarget;
        presenter.Target.Expression = Beutl.Engine.Expressions.Expression.CreateReference<Drawable>(expressionTarget.Id.Value);

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(ElementWith(_root, expressionTarget));
        scene.Children.Add(ElementWith(_root, presenter));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Does.Contain(expressionVideo));
    }

    // A cyclic reference-expression chain (a presenter Target whose expression resolves back to its own
    // Target property) must not overflow the stack while enumerating. On cycle re-entry the render's
    // IsEvaluating guard yields DefaultValue (null), never the Value, so a missing Value
    // file must not be reported — resolution must not fall back to it.
    [Test]
    public void CollectRenderableSources_CyclicTargetExpression_DoesNotReportCurrentValueFile()
    {
        string missingCurrent = Path.Combine(_root, "current.mov");

        var presenter = new DrawablePresenter();
        // Value holds a missing file; the expression self-cycles back to the Target property. The
        // render evaluates the expression to DefaultValue (null) on cycle, so the missing file never opens.
        presenter.Target.Value = VideoDrawable(missingCurrent);
        presenter.Target.Expression = Beutl.Engine.Expressions.Expression.CreateReference<Drawable>(presenter.Id.Value, "Target");

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(ElementWith(_root, presenter));
        // Without a root the self-reference is unresolvable and never reaches the cycle branch under test.
        var hierarchyRoot = new TestHierarchicalRoot();
        hierarchyRoot.HierarchicalChildren.Add(scene);

        IReadOnlyList<string> missing = null!;
        Assert.DoesNotThrow(() =>
            missing = ExportSourceValidator.GetMissingPaths(
                ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene)));
        Assert.That(missing, Does.Not.Contain(missingCurrent));
    }

    // A reference-expression whose target id cannot be resolved evaluates to DefaultValue (null), so
    // IExpressionProperty.Evaluate never samples the stale Value while the expression is set. A presenter
    // Target with a broken reference and a Value holding a missing file must therefore not report
    // that file — the render opens nothing.
    [Test]
    public void CollectRenderableSources_UnresolvablePresenterTargetExpression_DoesNotReportCurrentValueFile()
    {
        string missingCurrent = Path.Combine(_root, "current.mov");

        var presenter = new DrawablePresenter();
        presenter.Target.Value = VideoDrawable(missingCurrent);
        presenter.Target.Expression = Beutl.Engine.Expressions.Expression.CreateReference<Drawable>(Guid.NewGuid());

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(ElementWith(_root, presenter));
        var hierarchyRoot = new TestHierarchicalRoot();
        hierarchyRoot.HierarchicalChildren.Add(scene);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Does.Not.Contain(missingCurrent));
    }

    // DrawableTimeController.PostReconcile renders context.Get(Target); with a broken reference the effective
    // target is DefaultValue (null), so the stale Value target's missing file must not be reported.
    [Test]
    public void CollectRenderableSources_UnresolvableTimeControllerTargetExpression_DoesNotReportCurrentValueFile()
    {
        string missingCurrent = Path.Combine(_root, "current.mov");

        var controller = new DrawableTimeController();
        controller.Target.Value = VideoDrawable(missingCurrent);
        controller.Target.Expression = Beutl.Engine.Expressions.Expression.CreateReference<Drawable>(Guid.NewGuid());

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(ElementWith(_root, controller));
        var hierarchyRoot = new TestHierarchicalRoot();
        hierarchyRoot.HierarchicalChildren.Add(scene);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Does.Not.Contain(missingCurrent));
    }

    // FilterEffectPresenter and DelayAnimationEffect render their generated resource target from the
    // effective property value; a broken reference evaluates to DefaultValue, so a missing media source
    // inside the stale Value filter must not be reported.
    [Test]
    public void CollectRenderableSources_UnresolvableFilterTargetExpression_DoesNotReportCurrentValueFile()
    {
        string presenterMissing = Path.Combine(_root, "presenter-fx.mov");
        string delayMissing = Path.Combine(_root, "delay-fx.mov");

        var presenter = new Beutl.Graphics.Effects.FilterEffectPresenter();
        presenter.Target.Value = FilterWithMissingSource(presenterMissing);
        presenter.Target.Expression = Beutl.Engine.Expressions.Expression.CreateReference<FilterEffect>(Guid.NewGuid());

        var delay = new Beutl.Graphics.Effects.DelayAnimationEffect();
        delay.Effect.Value = FilterWithMissingSource(delayMissing);
        delay.Effect.Expression = Beutl.Engine.Expressions.Expression.CreateReference<FilterEffect>(Guid.NewGuid());

        var drawable = new SourceVideo();
        var group = (FilterEffectGroup)drawable.FilterEffect.Value!;
        group.Children.Add(presenter);
        group.Children.Add(delay);

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        scene.Children.Add(ElementWith(_root, drawable));
        var hierarchyRoot = new TestHierarchicalRoot();
        hierarchyRoot.HierarchicalChildren.Add(scene);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Does.Not.Contain(presenterMissing).And.Not.Contain(delayMissing));
    }

    // SceneDrawable/SceneSound composite the effective ReferencedScene value; a broken reference evaluates
    // to DefaultValue, so missing media inside the stale Value scene must not be reported.
    [Test]
    public void CollectRenderableSources_UnresolvableReferencedSceneExpression_DoesNotReportCurrentValueMedia()
    {
        string missingImage = Path.Combine(_root, "child.png");
        string missingSound = Path.Combine(_root, "child.mp3");

        var childScene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "child.scene")) };
        childScene.Children.Add(CreateImageElement(_root, missingImage));
        childScene.Children.Add(CreateSoundElement(_root, missingSound));

        var sceneDrawable = new SceneDrawable();
        sceneDrawable.ReferencedScene.Value = childScene;
        sceneDrawable.ReferencedScene.Expression = Beutl.Engine.Expressions.Expression.CreateReference<Scene>(Guid.NewGuid());

        var sceneSound = new SceneSound();
        sceneSound.ReferencedScene.Value = childScene;
        sceneSound.ReferencedScene.Expression = Beutl.Engine.Expressions.Expression.CreateReference<Scene>(Guid.NewGuid());

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "root.scene")) };
        scene.Children.Add(ElementWith(_root, sceneDrawable));
        scene.Children.Add(ElementWith(_root, sceneSound));
        var hierarchyRoot = new TestHierarchicalRoot();
        hierarchyRoot.HierarchicalChildren.Add(scene);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Does.Not.Contain(missingImage).And.Not.Contain(missingSound));
    }

    private static Beutl.Graphics.Effects.FilterEffect FilterWithMissingSource(string missingPath)
    {
        var node = new Beutl.NodeGraph.Nodes.VideoSourceNode();
        node.Source.Property!.SetValue(MakeVideoSource(missingPath));
        var graphEffect = new Beutl.NodeGraph.NodeGraphFilterEffect();
        graphEffect.Model.Value!.Nodes.Add(node);
        return graphEffect;
    }

    // A save-frame preflight passes a zero-duration window; the referenced-scene child gate must
    // point-sample (Contains) like SortLayers, or a child active at the sampled time is wrongly skipped
    // and its missing media escapes preflight.
    [Test]
    public void CollectRenderableSources_SaveFrame_ReferencedSceneChildAtSampleTime_IsReported()
    {
        string missingImage = Path.Combine(_root, "child.png");

        var childScene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "child.scene")) };
        Element child = CreateImageElement(_root, missingImage);
        child.Start.Value = MediaTime.Zero;
        child.Length.Value = MediaTime.FromSeconds(5);
        childScene.Children.Add(child);

        var sceneDrawable = new SceneDrawable();
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "root.scene")) };
        sceneDrawable.ReferencedScene.Expression = SceneReferenceTestHelper.Create(scene, childScene);
        Element outer = ElementWith(_root, sceneDrawable);
        outer.Start.Value = MediaTime.Zero;
        outer.Length.Value = MediaTime.FromSeconds(10);
        scene.Children.Add(outer);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, MediaTime.FromSeconds(1)));

        Assert.That(missing, Does.Contain(missingImage));
    }

    // An AudioVisualizerDrawable.Source that is a SceneSound composes the referenced scene's audio to draw
    // its waveform; the structural SceneSound audio walk never fires for a property value, so preflight
    // must dispatch it or the referenced scene's missing audio is missed.
    [Test]
    public void CollectRenderableSources_VisualizerSceneSoundSource_ReportsReferencedAudio()
    {
        string missingSound = Path.Combine(_root, "vis.mp3");

        var childScene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "child.scene")) };
        childScene.Children.Add(CreateSoundElement(_root, missingSound));

        var sceneSound = new SceneSound();
        var visualizer = new Beutl.Graphics.AudioVisualizers.AudioWaveformDrawable();
        visualizer.Source.Value = sceneSound;

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "root.scene")) };
        sceneSound.ReferencedScene.Expression = SceneReferenceTestHelper.Create(scene, childScene);
        scene.Children.Add(ElementWith(_root, visualizer));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Does.Contain(missingSound));
    }

    // The property-held SceneSound composes the referenced scene from its own window start too, so a
    // sound clip before it must not be preflighted.
    [Test]
    public void CollectRenderableSources_VisualizerSceneSoundSourceWithStart_ExcludesPreWindowClips()
    {
        string beforeWindow = Path.Combine(_root, "before.mp3");
        string insideWindow = Path.Combine(_root, "inside.mp3");

        var childScene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(_root, "child.scene")),
            Start = { Value = MediaTime.FromSeconds(5) },
        };
        childScene.Children.Add(CreateSoundElement(_root, beforeWindow));
        Element inside = CreateSoundElement(_root, insideWindow);
        inside.Start.Value = MediaTime.FromSeconds(5);
        childScene.Children.Add(inside);

        var sceneSound = new SceneSound();
        var visualizer = new Beutl.Graphics.AudioVisualizers.AudioWaveformDrawable();
        visualizer.Source.Value = sceneSound;

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "root.scene")) };
        sceneSound.ReferencedScene.Expression = SceneReferenceTestHelper.Create(scene, childScene);
        scene.Children.Add(ElementWith(_root, visualizer, length: MediaTime.FromSeconds(10)));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Is.EqualTo(new[] { insideWindow }));
    }

    // A DrawableBrush paints a shape with a nested Drawable that BrushConstructor renders; it is reachable
    // only as a property value, so preflight must dispatch it or a missing source inside it is missed.
    [Test]
    public void CollectRenderableSources_DrawableBrushDrawable_ReportsNestedSource()
    {
        string missingVideo = Path.Combine(_root, "brush.mov");

        var brush = new DrawableBrush();
        brush.Drawable.Value = VideoDrawable(missingVideo);
        var shape = new RectShape();
        shape.Fill.Value = brush;

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "root.scene")) };
        scene.Children.Add(ElementWith(_root, shape));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Does.Contain(missingVideo));
    }

    // A GeometryShapeNode.Fill node input set to a DrawableBrush is rendered through BrushConstructor, so
    // the node-input walk must thread the walk context and dispatch the nested Drawable.
    [Test]
    public void CollectRenderableSources_NodeInputDrawableBrush_ReportsNestedSource()
    {
        string missingVideo = Path.Combine(_root, "node-brush.mov");

        var brush = new DrawableBrush();
        brush.Drawable.Value = VideoDrawable(missingVideo);
        var node = new Beutl.NodeGraph.Nodes.GeometryShapeNode();
        node.Fill.Property!.SetValue(brush);
        var drawable = new NodeGraphDrawable();
        drawable.Model.Value!.Nodes.Add(node);

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "root.scene")) };
        scene.Children.Add(ElementWith(_root, drawable));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Does.Contain(missingVideo));
    }

    // A disabled drawable never renders, so export preflight (skipDisabledElements) must not demand its
    // file even when it is reached through a node input — the node walk must honour that gate, not hard-
    // code it off.
    [Test]
    public void CollectRenderableSources_DisabledDrawableInNodeInput_IsNotReported()
    {
        string missingVideo = Path.Combine(_root, "disabled-node-brush.mov");

        SourceVideo disabledInner = VideoDrawable(missingVideo);
        disabledInner.IsEnabled.Value = false;
        var brush = new DrawableBrush();
        brush.Drawable.Value = disabledInner;
        var node = new Beutl.NodeGraph.Nodes.GeometryShapeNode();
        node.Fill.Property!.SetValue(brush);
        var drawable = new NodeGraphDrawable();
        drawable.Model.Value!.Nodes.Add(node);

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "root.scene")) };
        scene.Children.Add(ElementWith(_root, drawable));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Does.Not.Contain(missingVideo));
    }

    // A reference-expression whose target property value is a DrawableBrush resolves at render, so the
    // expression walk must thread the walk context and dispatch the nested Drawable. The holder is reachable
    // by id but not walked as a scene element, so the only path to its media is the expression.
    [Test]
    public void CollectRenderableSources_ExpressionResolvedDrawableBrush_ReportsNestedSource()
    {
        string missingVideo = Path.Combine(_root, "expr-brush.mov");

        var holderBrush = new DrawableBrush();
        holderBrush.Drawable.Value = VideoDrawable(missingVideo);
        var holder = new RectShape();
        holder.Fill.Value = holderBrush;

        var refShape = new RectShape();
        refShape.Fill.Expression = Beutl.Engine.Expressions.Expression.CreateReference<Brush>(holder.Id.Value, "Fill");

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "root.scene")) };
        scene.Children.Add(ElementWith(_root, refShape));
        var hierarchyRoot = new TestHierarchicalRoot();
        hierarchyRoot.HierarchicalChildren.Add(scene);
        hierarchyRoot.HierarchicalChildren.Add(holder);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Does.Contain(missingVideo));
    }

    // A video-muted layer contributes no picture (SceneCompositor.SortLayers skips it for the graphics
    // pass), so a missing video original on that layer must not block export or a save frame.
    [Test]
    public void CollectRenderableSources_VideoMutedLayer_SkipsVideoSource()
    {
        string missingVideo = Path.Combine(_root, "muted.mov");

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        Element element = CreateVideoElement(_root, missingVideo);
        element.ZIndex.Value = 3;
        scene.Children.Add(element);
        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 3 }, IsVideoMuted = { Value = true } });

        IReadOnlyList<string> export = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));
        IReadOnlyList<string> frame = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, MediaTime.Zero));

        Assert.Multiple(() =>
        {
            Assert.That(export, Is.Empty);
            Assert.That(frame, Is.Empty);
        });
    }

    // Mute is per-pass: a video-muted layer still plays its sound, so its audio original is still opened by
    // the audio pass and must remain a preflight blocker even though its picture is skipped.
    [Test]
    public void CollectRenderableSources_VideoMutedLayer_StillChecksAudioSource()
    {
        string missingSound = Path.Combine(_root, "muted-video.mp3");

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        Element element = CreateSoundElement(_root, missingSound);
        element.ZIndex.Value = 2;
        scene.Children.Add(element);
        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 2 }, IsVideoMuted = { Value = true } });

        IReadOnlyList<string> export = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(export, Is.EqualTo(new[] { missingSound }));
    }

    // An audio-muted layer contributes no sound, so a missing audio original on it must not block export.
    [Test]
    public void CollectRenderableSources_AudioMutedLayer_SkipsAudioSource()
    {
        string missingSound = Path.Combine(_root, "muted.mp3");

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        Element element = CreateSoundElement(_root, missingSound);
        element.ZIndex.Value = 1;
        scene.Children.Add(element);
        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 1 }, IsAudioMuted = { Value = true } });

        IReadOnlyList<string> export = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(export, Is.Empty);
    }

    // With any solo layer active only solo layers render, so a missing original on a non-solo layer must
    // not block export while the soloed layer's missing original still does.
    [Test]
    public void CollectRenderableSources_SoloLayer_SkipsNonSoloElements()
    {
        string soloMissing = Path.Combine(_root, "solo.mov");
        string otherMissing = Path.Combine(_root, "other.mov");

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        Element soloed = CreateVideoElement(_root, soloMissing);
        soloed.ZIndex.Value = 0;
        Element other = CreateVideoElement(_root, otherMissing);
        other.ZIndex.Value = 1;
        scene.Children.Add(soloed);
        scene.Children.Add(other);
        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 0 }, IsSolo = { Value = true } });
        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 1 } });

        IReadOnlyList<string> export = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(export, Is.EqualTo(new[] { soloMissing }));
    }

    // A referenced scene composes through its OWN SceneCompositor.SortLayers, so a muted / non-solo layer
    // inside an embedded scene is skipped at render time — a missing file on such a nested layer must not
    // block export either, mirroring the root-scene behavior.
    [Test]
    public void CollectRenderableSources_VideoMutedLayerInReferencedScene_SkipsVideoSource()
    {
        string missingImage = Path.Combine(_root, "muted-nested.png");

        var childScene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "child.scene")) };
        Element child = CreateImageElement(_root, missingImage);
        child.ZIndex.Value = 4;
        childScene.Children.Add(child);
        childScene.Layers.Add(new TimelineLayer { ZIndex = { Value = 4 }, IsVideoMuted = { Value = true } });

        var sceneDrawable = new SceneDrawable();
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "root.scene")) };
        sceneDrawable.ReferencedScene.Expression = SceneReferenceTestHelper.Create(scene, childScene);
        scene.Children.Add(ElementWith(_root, sceneDrawable));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Is.Empty);
    }

    // A solo layer inside a referenced scene means only that scene's solo layers render; a missing file on
    // a non-solo nested layer must not block export while the soloed nested layer's missing file still does.
    [Test]
    public void CollectRenderableSources_SoloLayerInReferencedScene_SkipsNonSoloNestedElements()
    {
        string soloMissing = Path.Combine(_root, "nested-solo.png");
        string otherMissing = Path.Combine(_root, "nested-other.png");

        var childScene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "child.scene")) };
        Element soloed = CreateImageElement(_root, soloMissing);
        soloed.ZIndex.Value = 0;
        Element other = CreateImageElement(_root, otherMissing);
        other.ZIndex.Value = 1;
        childScene.Children.Add(soloed);
        childScene.Children.Add(other);
        childScene.Layers.Add(new TimelineLayer { ZIndex = { Value = 0 }, IsSolo = { Value = true } });
        childScene.Layers.Add(new TimelineLayer { ZIndex = { Value = 1 } });

        var sceneDrawable = new SceneDrawable();
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "root.scene")) };
        sceneDrawable.ReferencedScene.Expression = SceneReferenceTestHelper.Create(scene, childScene);
        scene.Children.Add(ElementWith(_root, sceneDrawable));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Is.EqualTo(new[] { soloMissing }));
    }

    // The audio-target branch of the nested-scene skip: a SceneSound composes the referenced scene's
    // audio through its SortLayers, so an audio-muted layer inside that scene contributes no sound and its
    // missing audio original must not block export.
    [Test]
    public void CollectRenderableSources_AudioMutedLayerInReferencedScene_SkipsAudioSource()
    {
        string missingSound = Path.Combine(_root, "muted-nested.mp3");

        var childScene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "child.scene")) };
        Element child = CreateSoundElement(_root, missingSound);
        child.ZIndex.Value = 5;
        childScene.Children.Add(child);
        childScene.Layers.Add(new TimelineLayer { ZIndex = { Value = 5 }, IsAudioMuted = { Value = true } });

        // A default SceneSound has the identity audio map, so it windows the referenced scene per-target.
        var sceneSound = new SceneSound();
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(_root, "root.scene")) };
        sceneSound.ReferencedScene.Expression = SceneReferenceTestHelper.Create(scene, childScene);
        scene.Children.Add(ElementWith(_root, sceneSound));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Is.Empty);
    }

    private static SourceSound SoundDrawable(string sourcePath)
    {
        var source = new SoundSource();
        source.ReadFrom(new Uri(sourcePath));
        var sound = new SourceSound();
        sound.Source.Value = source;
        return sound;
    }

    private static VideoSource MakeVideoSource(string sourcePath)
    {
        var source = new VideoSource();
        source.ReadFrom(new Uri(sourcePath));
        return source;
    }

    private static SourceVideo VideoDrawable(string sourcePath)
    {
        var drawable = new SourceVideo();
        drawable.Source.Value = MakeVideoSource(sourcePath);
        return drawable;
    }

    private static Element CreateVideoElement(string root, string sourcePath)
        => ElementWith(root, VideoDrawable(sourcePath));

    private static Element CreateImageElement(string root, string sourcePath)
    {
        var source = new ImageSource();
        source.ReadFrom(new Uri(sourcePath));
        var drawable = new SourceImage();
        drawable.Source.Value = source;
        return ElementWith(root, drawable);
    }

    private static Element CreateSoundElement(string root, string sourcePath)
        => ElementWith(root, SoundDrawable(sourcePath));

    private static Element ElementWith(
        string root, Beutl.Engine.EngineObject obj, MediaTime? start = null, MediaTime? length = null)
    {
        var element = new Element
        {
            Start = { Value = start ?? MediaTime.Zero },
            Length = { Value = length ?? MediaTime.FromSeconds(1) },
            IsEnabled = { Value = true },
            Uri = new Uri(Path.Combine(root, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(obj);
        return element;
    }
}
