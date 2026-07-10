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

namespace Beutl.UnitTests.Editor;

[TestFixture]
public sealed class ExportSourceValidatorTests
{
    private static readonly TimeRange s_wholeScene = new(TimeSpan.Zero, TimeSpan.FromSeconds(10));

    [Test]
    public void GetMissingPaths_ReturnsMissingSourcesReferencedByScene()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string existingPath = Path.Combine(root, "existing.mov");
        string missingPath = Path.Combine(root, "missing.mov");
        File.WriteAllBytes(existingPath, [1]);

        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(root, "test.scene")),
        };
        scene.Children.Add(CreateVideoElement(root, existingPath));
        scene.Children.Add(CreateVideoElement(root, missingPath));

        IReadOnlySet<string> referenced = ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene);
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(referenced);

        Assert.That(missing, Is.EqualTo(new[] { missingPath }));
    }

    // A SceneDrawable renders only the referenced scene's graphics (SceneDrawable.EvaluateGraphics), so
    // a missing image inside it blocks export, but an audio-only Sound the video render never opens must
    // not — even for a full export. A SceneSound (below) is what pulls that scene's audio.
    [Test]
    public void GetMissingPaths_GraphicallyEmbeddedScene_ReportsImageButNotAudioOnlySound()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingImage = Path.Combine(root, "missing.png");
        string missingSound = Path.Combine(root, "missing.mp3");

        var childScene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(root, "child.scene")),
        };
        childScene.Children.Add(CreateImageElement(root, missingImage));
        childScene.Children.Add(CreateSoundElement(root, missingSound));

        var sceneDrawable = new SceneDrawable();
        sceneDrawable.ReferencedScene.CurrentValue = childScene;
        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(root, "root.scene")),
        };
        scene.Children.Add(ElementWith(root, sceneDrawable));

        IReadOnlySet<string> referenced = ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene);
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(referenced);

        Assert.That(missing, Is.EqualTo(new[] { missingImage }));
    }

    // A SceneSound renders only the referenced scene's audio (SceneSound.EvaluateAudio), so its missing
    // sound blocks export while the scene's graphics-only image, which this embed never renders, does not.
    [Test]
    public void GetMissingPaths_AudiallyEmbeddedScene_ReportsSoundButNotGraphicsOnlyImage()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingImage = Path.Combine(root, "missing.png");
        string missingSound = Path.Combine(root, "missing.mp3");

        var childScene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(root, "child.scene")),
        };
        childScene.Children.Add(CreateImageElement(root, missingImage));
        childScene.Children.Add(CreateSoundElement(root, missingSound));

        var sceneSound = new SceneSound();
        sceneSound.ReferencedScene.CurrentValue = childScene;
        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(root, "root.scene")),
        };
        scene.Children.Add(ElementWith(root, sceneSound));

        IReadOnlySet<string> referenced = ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene);
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(referenced);

        Assert.That(missing, Is.EqualTo(new[] { missingSound }));
    }

    // A disabled element never renders (SceneCompositor.SortLayers gates on IsEnabled), so its missing
    // original must not block export of the rest of the scene.
    [Test]
    public void CollectRenderableSources_SkipsDisabledElement()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingPath = Path.Combine(root, "missing.mov");

        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(root, "test.scene")),
        };
        Element disabled = CreateVideoElement(root, missingPath);
        disabled.IsEnabled = false;
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
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingPath = Path.Combine(root, "missing.mov");

        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(root, "test.scene")),
        };
        Element outOfRange = CreateVideoElement(root, missingPath);
        outOfRange.Start = TimeSpan.FromSeconds(20);
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
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string atFrame = Path.Combine(root, "at-frame.mov");
        string laterOnly = Path.Combine(root, "later-only.mov");

        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(root, "test.scene")),
        };
        Element visible = CreateVideoElement(root, atFrame);
        visible.Start = TimeSpan.Zero;
        visible.Length = TimeSpan.FromSeconds(2);
        scene.Children.Add(visible);

        Element later = CreateVideoElement(root, laterOnly);
        later.Start = TimeSpan.FromSeconds(5);
        later.Length = TimeSpan.FromSeconds(2);
        scene.Children.Add(later);

        IReadOnlySet<string> referenced =
            ExportSourceValidator.CollectRenderableSources(scene, TimeSpan.FromSeconds(1));

        Assert.That(referenced, Is.EquivalentTo(new[] { atFrame }));
    }

    // Fix #2: a disabled clip inside a referenced scene never renders (SceneCompositor.SortLayers gates
    // on IsEnabled within the referenced scene too), so its missing original must not block export while
    // an enabled sibling's missing original still does.
    [Test]
    public void CollectRenderableSources_SkipsDisabledElementInReferencedScene()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingEnabled = Path.Combine(root, "enabled.png");
        string missingDisabled = Path.Combine(root, "disabled.png");

        var childScene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(root, "child.scene")),
        };
        childScene.Children.Add(CreateImageElement(root, missingEnabled));
        Element disabled = CreateImageElement(root, missingDisabled);
        disabled.IsEnabled = false;
        childScene.Children.Add(disabled);

        var sceneDrawable = new SceneDrawable();
        sceneDrawable.ReferencedScene.CurrentValue = childScene;
        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(root, "root.scene")),
        };
        scene.Children.Add(ElementWith(root, sceneDrawable));

        IReadOnlySet<string> referenced = ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene);
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(referenced);

        Assert.That(missing, Is.EqualTo(new[] { missingEnabled }));
    }

    // Fix #3: an enabled element can hold a disabled object; the render path (Element.CollectObjects)
    // skips disabled objects before opening media, so a missing file on a disabled object must not
    // block export even though its owning element renders.
    [Test]
    public void CollectRenderableSources_SkipsDisabledObjectInEnabledElement()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string enabledMissing = Path.Combine(root, "enabled.mov");
        string disabledMissing = Path.Combine(root, "disabled.mov");

        SourceVideo enabled = VideoDrawable(enabledMissing);
        SourceVideo disabled = VideoDrawable(disabledMissing);
        disabled.IsEnabled = false;
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(1),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(root, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(enabled);
        element.AddObject(disabled);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(element);

        IReadOnlySet<string> referenced = ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene);
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(referenced);

        Assert.That(missing, Is.EqualTo(new[] { enabledMissing }));
    }

    // Fix #4: save-frame renders graphics only (EvaluateGraphics filters CompositionTarget.Audio via
    // Element.CollectObjects), so a missing audio original must not block a still image; a full export
    // (which composes audio) still requires it.
    [Test]
    public void CollectRenderableSources_SingleFrame_ExcludesAudioSourcesButExportIncludesThem()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingVideo = Path.Combine(root, "missing.mov");
        string missingSound = Path.Combine(root, "missing.mp3");

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(CreateVideoElement(root, missingVideo));
        scene.Children.Add(CreateSoundElement(root, missingSound));

        IReadOnlyList<string> frame = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, TimeSpan.Zero));
        IReadOnlyList<string> export = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.Multiple(() =>
        {
            Assert.That(frame, Is.EqualTo(new[] { missingVideo }));
            Assert.That(export, Is.EquivalentTo(new[] { missingVideo, missingSound }));
        });
    }

    // Fix #5: export renders [Scene.Start, Scene.Start + Duration); a preflight range that starts at
    // Scene.Start includes clips in the exported segment and excludes ones entirely before it.
    [Test]
    public void CollectRenderableSources_RangeStartingAtSceneStart_TracksExportedSegment()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string beforeStart = Path.Combine(root, "before.mov");
        string inSegment = Path.Combine(root, "in-segment.mov");

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        Element before = CreateVideoElement(root, beforeStart);
        before.Start = TimeSpan.Zero;
        before.Length = TimeSpan.FromSeconds(2);
        scene.Children.Add(before);
        Element inside = CreateVideoElement(root, inSegment);
        inside.Start = TimeSpan.FromSeconds(5);
        inside.Length = TimeSpan.FromSeconds(2);
        scene.Children.Add(inside);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(
                scene, new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5))));

        Assert.That(missing, Is.EqualTo(new[] { inSegment }));
    }

    // An animated Source keyframe is filtered to the render window. For an object value the animator
    // samples the NEXT keyframe's value, so the middle key (missing) governs [0s, 5s): a window inside
    // that span requires it, while a window entirely after it (sampling the later key) must not.
    [Test]
    public void CollectRenderableSources_FiltersAnimatedSourceKeyframesToRenderWindow()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string startExisting = Path.Combine(root, "start.mov");
        string midMissing = Path.Combine(root, "mid.mov");
        string endExisting = Path.Combine(root, "end.mov");
        File.WriteAllBytes(startExisting, [1]);
        File.WriteAllBytes(endExisting, [1]);

        var drawable = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.Zero, Value = MakeVideoSource(startExisting) });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(5), Value = MakeVideoSource(midMissing) });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(10), Value = MakeVideoSource(endExisting) });
        drawable.Source.Animation = animation;

        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(15),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(root, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(drawable);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(element);

        // [0s, 3s) is inside the mid key's [0s, 5s) span → the missing mid file is required.
        IReadOnlyList<string> inMidSpan = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(3))));
        // [6s, 10s) samples only the end key → the mid file is not required.
        IReadOnlyList<string> afterMid = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(4))));

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
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingImage = Path.Combine(root, "missing.png");
        string missingSound = Path.Combine(root, "missing.mp3");

        var childScene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "child.scene")) };
        childScene.Children.Add(CreateImageElement(root, missingImage));
        childScene.Children.Add(CreateSoundElement(root, missingSound));

        var sceneDrawable = new SceneDrawable();
        sceneDrawable.ReferencedScene.CurrentValue = childScene;
        var sceneSound = new SceneSound();
        sceneSound.ReferencedScene.CurrentValue = childScene;
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "root.scene")) };
        scene.Children.Add(ElementWith(root, sceneDrawable));
        scene.Children.Add(ElementWith(root, sceneSound));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Is.EquivalentTo(new[] { missingImage, missingSound }));
    }

    // A SoundGroup renders its child Sounds through SoundGroup.Compose (the audio analogue of a
    // DrawableGroup), so a missing original nested in one must be preflighted, not surface at encode.
    [Test]
    public void GetMissingPaths_ReportsMissingSoundNestedInSoundGroup()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingSound = Path.Combine(root, "missing.mp3");

        var group = new SoundGroup();
        group.Children.Add(SoundDrawable(missingSound));
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(ElementWith(root, group));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Is.EqualTo(new[] { missingSound }));
    }

    // Global-clock keyframes use scene time, not element-local time, so a source key sampled inside the
    // exported interval must not be dropped by subtracting the element start; it must still block export.
    [Test]
    public void CollectRenderableSources_GlobalClockAnimatedSource_NotDroppedByLocalWindow()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string existing = Path.Combine(root, "existing.mov");
        string missingAtGlobal12 = Path.Combine(root, "missing.mov");
        File.WriteAllBytes(existing, [1]);

        var drawable = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?> { UseGlobalClock = true };
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(11), Value = MakeVideoSource(existing) });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(12), Value = MakeVideoSource(missingAtGlobal12) });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(13), Value = MakeVideoSource(existing) });
        drawable.Source.Animation = animation;

        var element = new Element
        {
            Start = TimeSpan.FromSeconds(10),
            Length = TimeSpan.FromSeconds(10),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(root, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(drawable);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(element);

        // Export [10s, 20s] samples the global key at 12s; subtracting the 10s element start would map
        // the local window to [0,10] and wrongly drop the 12s key.
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10))));

        Assert.That(missing, Is.EqualTo(new[] { missingAtGlobal12 }));
    }

    // When a property is animated by keyframes, the render samples the animation, not the base
    // CurrentValue, so a stale missing base must not block export while the keyframed values exist.
    [Test]
    public void CollectRenderableSources_AnimatedProperty_DoesNotReportOverriddenBaseValue()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingBase = Path.Combine(root, "old-base.mov");
        string keyframed = Path.Combine(root, "keyframed.mov");
        File.WriteAllBytes(keyframed, [1]);

        var drawable = new SourceVideo();
        drawable.Source.CurrentValue = MakeVideoSource(missingBase);
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.Zero, Value = MakeVideoSource(keyframed) });
        drawable.Source.Animation = animation;
        Element element = ElementWith(root, drawable);
        element.Length = TimeSpan.FromSeconds(10);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(element);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10))));

        Assert.That(missing, Is.Empty);
    }

    // The outgoing key governs the left-open span (-inf, 0s] and the incoming key takes over on (0s, +inf),
    // so an export window at [5s, 10s) samples only the incoming key and must not report the outgoing file.
    [Test]
    public void CollectRenderableSources_SourceSwitchExactlyAtWindowStart_DropsOutgoingKeyframe()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string outgoingMissing = Path.Combine(root, "outgoing.mov");
        string incoming = Path.Combine(root, "incoming.mov");
        File.WriteAllBytes(incoming, [1]);

        var drawable = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.Zero, Value = MakeVideoSource(outgoingMissing) });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(5), Value = MakeVideoSource(incoming) });
        drawable.Source.Animation = animation;
        Element element = ElementWith(root, drawable);
        element.Length = TimeSpan.FromSeconds(10);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(element);

        // Export [5s, 10s): the outgoing key governs [.., 5s) and is not sampled, so it must be dropped.
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5))));

        Assert.That(missing, Is.Empty);
    }

    // A DrawableDecorator renders its children at the same composition time, so the render window still
    // maps directly — an out-of-window animated keyframe under a decorator must be dropped, not reported.
    [Test]
    public void CollectRenderableSources_AnimatedSourceInDecorator_FiltersToRenderWindow()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string oldMissing = Path.Combine(root, "old.mov");
        string newExisting = Path.Combine(root, "new.mov");
        File.WriteAllBytes(newExisting, [1]);

        var child = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.Zero, Value = MakeVideoSource(oldMissing) });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(5), Value = MakeVideoSource(newExisting) });
        child.Source.Animation = animation;
        var decorator = new DrawableDecorator();
        decorator.Children.Add(child);
        Element element = ElementWith(root, decorator);
        element.Length = TimeSpan.FromSeconds(10);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(element);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(4))));

        Assert.That(missing, Is.Empty);
    }

    // Node-graph input-port animations must honour the render window too: an out-of-window keyframe on
    // a VideoSourceNode input must be dropped, so a since-replaced file it references cannot block export.
    [Test]
    public void CollectRenderableSources_AnimatedNodeGraphInput_FiltersToRenderWindow()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string oldMissing = Path.Combine(root, "old.mov");
        string newExisting = Path.Combine(root, "new.mov");
        File.WriteAllBytes(newExisting, [1]);

        var node = new VideoSourceNode();
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.Zero, Value = MakeVideoSource(oldMissing) });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(5), Value = MakeVideoSource(newExisting) });
        ((IAnimatablePropertyAdapter<VideoSource?>)node.Source.Property!).Animation = animation;
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue!.Nodes.Add(node);
        Element element = ElementWith(root, drawable);
        element.Length = TimeSpan.FromSeconds(10);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(element);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(4))));

        Assert.That(missing, Is.Empty);
    }

    // A rendered source nested inside an EngineObject-valued property (a Shape's Fill ImageBrush holding
    // ImageBrush.Source) is opened at render, so a missing such file must be preflighted, not slip past.
    [Test]
    public void CollectRenderableSources_ReportsMissingSourceInsideBrushProperty()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingImage = Path.Combine(root, "brush.png");

        var brush = new ImageBrush();
        var imageSource = new ImageSource();
        imageSource.ReadFrom(new Uri(missingImage));
        brush.Source.CurrentValue = imageSource;
        var shape = new RectShape();
        shape.Fill.CurrentValue = brush;
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(ElementWith(root, shape));

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
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string a = Path.Combine(root, "a.mov");
        string missingB = Path.Combine(root, "b.mov");
        string c = Path.Combine(root, "c.mov");
        File.WriteAllBytes(a, [1]);
        File.WriteAllBytes(c, [1]);

        var drawable = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.Zero, Value = MakeVideoSource(a) });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(5), Value = MakeVideoSource(missingB) });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(10), Value = MakeVideoSource(c) });
        drawable.Source.Animation = animation;
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(15),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(root, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(drawable);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(element);

        // At the exact frame t=5s the animator samples key b (its span (0s, 5s] contains the point); a
        // half-open window would drop it as an empty range, and the wrong boundary would sample c instead.
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, TimeSpan.FromSeconds(5)));

        Assert.That(missing, Is.EqualTo(new[] { missingB }));
    }

    // A null-valued keyframe makes GetAnimatedValue fall back to the base CurrentValue, so the base
    // must not be suppressed as overridden — its missing file still blocks export.
    [Test]
    public void CollectRenderableSources_AnimationWithNullKeyframe_KeepsBaseSource()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingBase = Path.Combine(root, "base.mov");

        var drawable = new SourceVideo();
        drawable.Source.CurrentValue = MakeVideoSource(missingBase);
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.Zero, Value = null });
        drawable.Source.Animation = animation;
        Element element = ElementWith(root, drawable);
        element.Length = TimeSpan.FromSeconds(10);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(element);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10))));

        Assert.That(missing, Is.EqualTo(new[] { missingBase }));
    }

    // The window handed to a SceneDrawable's referenced scene is already element-local (sceneWindow -
    // element.Start); subtracting sceneDrawable.Start again double-shifts it and drops in-window
    // keyframes for a non-zero-start element. The inner key at 2s sits inside the [0,10] local window
    // and must be reported — a double-subtraction would map the window to [-10,0] and drop it.
    [Test]
    public void CollectRenderableSources_NonZeroStartElement_ReferencedSceneWindowNotDoubleShifted()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string existing = Path.Combine(root, "existing.mov");
        string missingAt2s = Path.Combine(root, "at-2s.mov");
        File.WriteAllBytes(existing, [1]);

        var childDrawable = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.Zero, Value = MakeVideoSource(existing) });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(2), Value = MakeVideoSource(missingAt2s) });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(8), Value = MakeVideoSource(existing) });
        childDrawable.Source.Animation = animation;
        var childElement = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(10),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(root, $"{Guid.NewGuid():N}.layer")),
        };
        childElement.AddObject(childDrawable);
        var childScene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "child.scene")) };
        childScene.Children.Add(childElement);

        var sceneDrawable = new SceneDrawable();
        sceneDrawable.ReferencedScene.CurrentValue = childScene;
        var outerElement = new Element
        {
            Start = TimeSpan.FromSeconds(10),
            Length = TimeSpan.FromSeconds(10),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(root, $"{Guid.NewGuid():N}.layer")),
        };
        outerElement.AddObject(sceneDrawable);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "root.scene")) };
        scene.Children.Add(outerElement);

        // Export [10s, 20s]: the outer element's local window [0,10] is also the referenced scene's
        // window, so the inner key at 2s stays in window. Subtracting the 10s element start again would
        // shift it to [-10,0] and drop the missing key.
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10))));

        Assert.That(missing, Is.EqualTo(new[] { missingAt2s }));
    }

    // A global-clock keyframe is sampled at scene time. For an object value the animator returns the
    // NEXT key's value, so a key governs [previous key time, this key time); the last one holds to +inf.
    // A key whose span is entirely outside the exported scene-time window must be dropped.
    [Test]
    public void CollectRenderableSources_GlobalClockKeyframeOutsideSceneWindow_IsDropped()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string existing = Path.Combine(root, "existing.mov");
        string missingAtGlobal30 = Path.Combine(root, "missing.mov");
        File.WriteAllBytes(existing, [1]);

        var drawable = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?> { UseGlobalClock = true };
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(10), Value = MakeVideoSource(existing) });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(20), Value = MakeVideoSource(existing) });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(30), Value = MakeVideoSource(missingAtGlobal30) });
        drawable.Source.Animation = animation;

        var element = new Element
        {
            Start = TimeSpan.FromSeconds(5),
            Length = TimeSpan.FromSeconds(30),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(root, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(drawable);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(element);

        // Export [10s,12s]: the 30s global key governs [20s, +inf) — no exported frame samples it, so its
        // missing file must not block export. Previously global-clock keyframes were never windowed.
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2))));

        Assert.That(missing, Is.Empty);
    }

    // Without clamping, an export window wider than the element maps to pre-Start (negative) local time
    // and keeps element-local keyframes never rendered. Element Start=10s, export [15s, 20s]: only local
    // [5s, 10s] renders. Unclamped, SubtractStart(10) on the scene window [0s, 20s] would give [-10s, 10s]
    // and pull in the local-0s key; clamping to the element's active interval first excludes it.
    [Test]
    public void CollectRenderableSources_LateStartingElement_WindowClampedToActiveInterval()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string existing = Path.Combine(root, "existing.mov");
        string missingEarly = Path.Combine(root, "early.mov");
        File.WriteAllBytes(existing, [1]);

        // Element starts at scene 10s. Local-0s key governs (-inf, 0s]; local-3s key holds from (0s, 3s];
        // local-12s key holds from (3s, +inf). Only the last two are sampled inside local [5s, 10s].
        var drawable = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.Zero, Value = MakeVideoSource(missingEarly) });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(3), Value = MakeVideoSource(existing) });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(12), Value = MakeVideoSource(existing) });
        drawable.Source.Animation = animation;
        var element = new Element
        {
            Start = TimeSpan.FromSeconds(10),
            Length = TimeSpan.FromSeconds(15),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(root, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(drawable);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(element);

        // Export [15s, 20s]: element-local window [5s, 10s]. The early key governs local (-inf, 0s], never
        // sampled there, so its missing file must not block export.
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(5))));

        Assert.That(missing, Is.Empty);
    }

    // When a property carries an expression, IProperty.GetValue evaluates the expression and never
    // samples the base or the animation, so a missing base/keyframe source must not block export while
    // the expression resolves to an existing source.
    [Test]
    public void CollectRenderableSources_ExpressionOverridesBaseAndAnimation()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingBase = Path.Combine(root, "base.mov");
        string missingKey = Path.Combine(root, "key.mov");
        string exprTarget = Path.Combine(root, "expr.mov");
        File.WriteAllBytes(exprTarget, [1]);

        var referenced = new SourceVideo();
        referenced.Source.CurrentValue = MakeVideoSource(exprTarget);

        var drawable = new SourceVideo();
        drawable.Source.CurrentValue = MakeVideoSource(missingBase);
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.Zero, Value = MakeVideoSource(missingKey) });
        drawable.Source.Animation = animation;
        drawable.Source.Expression = Beutl.Engine.Expressions.Expression.CreateReference<VideoSource>(referenced.Id, "Source");

        Element referencedElement = ElementWith(root, referenced);
        Element element = ElementWith(root, drawable);
        element.Length = TimeSpan.FromSeconds(10);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(referencedElement);
        scene.Children.Add(element);

        // Windowed export: the expression wins, so the missing base/key must not block; only the resolved
        // (existing) expression target is opened.
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10))));

        Assert.That(missing, Is.Empty);
    }

    // A node-graph input on the global clock is sampled at scene time, so a global-clock source key
    // outside the exported scene window must be dropped, not fall back to a broad walk that blocks export.
    [Test]
    public void CollectRenderableSources_GlobalClockNodeInput_FilteredBySceneWindow()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string existing = Path.Combine(root, "existing.mov");
        string missingLate = Path.Combine(root, "late.mov");
        File.WriteAllBytes(existing, [1]);

        var node = new VideoSourceNode();
        var animation = new KeyFrameAnimation<VideoSource?> { UseGlobalClock = true };
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(10), Value = MakeVideoSource(existing) });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(20), Value = MakeVideoSource(existing) });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(30), Value = MakeVideoSource(missingLate) });
        ((IAnimatablePropertyAdapter<VideoSource?>)node.Source.Property!).Animation = animation;
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue!.Nodes.Add(node);
        var element = new Element
        {
            Start = TimeSpan.FromSeconds(5),
            Length = TimeSpan.FromSeconds(30),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(root, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(drawable);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(element);

        // Export [10s, 12s]: the 30s global key governs scene (20s, +inf), never sampled, so its missing
        // file must not block export — previously node inputs ignored the scene window.
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2))));

        Assert.That(missing, Is.Empty);
    }

    // A referenced-scene child whose range does not intersect the exported window never renders (SortLayers
    // gates on Range.Intersects), so its direct missing source must not block export.
    [Test]
    public void CollectRenderableSources_ReferencedSceneChildOutsideWindow_IsSkipped()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingImage = Path.Combine(root, "out-of-window.png");

        var childScene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "child.scene")) };
        Element outOfWindow = CreateImageElement(root, missingImage);
        outOfWindow.Start = TimeSpan.FromSeconds(30);
        outOfWindow.Length = TimeSpan.FromSeconds(5);
        childScene.Children.Add(outOfWindow);

        var sceneDrawable = new SceneDrawable();
        sceneDrawable.ReferencedScene.CurrentValue = childScene;
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "root.scene")) };
        Element outer = ElementWith(root, sceneDrawable);
        outer.Length = TimeSpan.FromSeconds(10);
        scene.Children.Add(outer);

        // Export [0s, 5s]: the referenced-scene child at 30s never composes, so its missing image is dropped.
        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5))));

        Assert.That(missing, Is.Empty);
    }

    // A SceneSound with the identity audio map (OffsetPosition 0, Speed 100, unanimated) samples the
    // referenced scene in element-local time, so a referenced-scene child outside the window is dropped;
    // a non-identity map (animated Speed) falls back to the full walk and keeps it.
    [Test]
    public void CollectRenderableSources_IdentitySceneSound_WindowsReferencedAudio()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingLate = Path.Combine(root, "late.mp3");

        Scene BuildRoot(bool animateSpeed)
        {
            var childScene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, $"child-{Guid.NewGuid():N}.scene")) };
            Element lateChild = CreateSoundElement(root, missingLate);
            lateChild.Start = TimeSpan.FromSeconds(30);
            lateChild.Length = TimeSpan.FromSeconds(5);
            childScene.Children.Add(lateChild);

            var sceneSound = new SceneSound();
            sceneSound.ReferencedScene.CurrentValue = childScene;
            if (animateSpeed)
            {
                var speed = new KeyFrameAnimation<float>();
                speed.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 100f });
                speed.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(5), Value = 200f });
                sceneSound.Speed.Animation = speed;
            }

            var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, $"root-{Guid.NewGuid():N}.scene")) };
            Element outer = ElementWith(root, sceneSound);
            outer.Length = TimeSpan.FromSeconds(40);
            scene.Children.Add(outer);
            return scene;
        }

        // Export [0s, 5s]: identity map windows the referenced scene, so the 30s child never composes and
        // its missing file is dropped; the animated-Speed map is a real remap and conservatively keeps it.
        IReadOnlyList<string> identityMissing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(BuildRoot(animateSpeed: false), new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5))));
        IReadOnlyList<string> remappedMissing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(BuildRoot(animateSpeed: true), new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5))));

        Assert.Multiple(() =>
        {
            Assert.That(identityMissing, Is.Empty);
            Assert.That(remappedMissing, Does.Contain(missingLate));
        });
    }

    // A DrawablePresenter whose Target is supplied by a reference-expression renders the resolved target,
    // so a missing source inside it must be preflighted even though Target.CurrentValue is null.
    [Test]
    public void CollectRenderableSources_ExpressionSuppliedPresenterTarget_IsWalked()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingVideo = Path.Combine(root, "presented.mov");

        var presented = VideoDrawable(missingVideo);
        var presenter = new DrawablePresenter();
        presenter.Target.Expression = Beutl.Engine.Expressions.Expression.CreateReference<Drawable>(presented.Id);

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(ElementWith(root, presented));
        scene.Children.Add(ElementWith(root, presenter));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Does.Contain(missingVideo));
    }

    // When a presenter Target has both a CurrentValue and a reference-expression, the render evaluates
    // the expression, so preflight must walk the expression's target — not the (stale) CurrentValue.
    [Test]
    public void CollectRenderableSources_PresenterTargetExpression_TakesPrecedenceOverCurrentValue()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string currentVideo = Path.Combine(root, "current.mov");
        string expressionVideo = Path.Combine(root, "expression.mov");
        File.WriteAllBytes(currentVideo, [1]);

        // CurrentValue points at an existing file; the expression points at a missing one. The render
        // opens the expression target, so its missing file must be reported.
        var currentTarget = VideoDrawable(currentVideo);
        var expressionTarget = VideoDrawable(expressionVideo);
        var presenter = new DrawablePresenter();
        presenter.Target.CurrentValue = currentTarget;
        presenter.Target.Expression = Beutl.Engine.Expressions.Expression.CreateReference<Drawable>(expressionTarget.Id);

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(ElementWith(root, expressionTarget));
        scene.Children.Add(ElementWith(root, presenter));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Does.Contain(expressionVideo));
    }

    // Reference-expression resolution needs owner.FindHierarchicalRoot() to succeed; a detached scene has
    // no root, so tests that exercise resolution must attach the scene under one of these.
    private sealed class TestHierarchicalRoot : Hierarchical, IHierarchicalRoot
    {
        public new Beutl.Collections.ICoreList<IHierarchical> HierarchicalChildren => base.HierarchicalChildren;

        public event EventHandler<IHierarchical>? DescendantAttached;
        public event EventHandler<IHierarchical>? DescendantDetached;

        public void OnDescendantAttached(IHierarchical descendant) => DescendantAttached?.Invoke(this, descendant);

        public void OnDescendantDetached(IHierarchical descendant) => DescendantDetached?.Invoke(this, descendant);
    }

    // A cyclic reference-expression chain (a presenter Target whose expression resolves back to its own
    // Target property) must not overflow the stack while enumerating. On cycle re-entry the render's
    // IsEvaluating guard yields DefaultValue (null), never the CurrentValue, so a missing CurrentValue
    // file must not be reported — resolution must not fall back to it.
    [Test]
    public void CollectRenderableSources_CyclicTargetExpression_DoesNotReportCurrentValueFile()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingCurrent = Path.Combine(root, "current.mov");

        var presenter = new DrawablePresenter();
        // CurrentValue holds a missing file; the expression self-cycles back to the Target property. The
        // render evaluates the expression to DefaultValue (null) on cycle, so the missing file never opens.
        presenter.Target.CurrentValue = VideoDrawable(missingCurrent);
        presenter.Target.Expression = Beutl.Engine.Expressions.Expression.CreateReference<Drawable>(presenter.Id, "Target");

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(ElementWith(root, presenter));
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
    // IProperty.GetValue never samples the stale CurrentValue while the expression is set. A presenter
    // Target with a broken reference and a CurrentValue holding a missing file must therefore not report
    // that file — the render opens nothing.
    [Test]
    public void CollectRenderableSources_UnresolvablePresenterTargetExpression_DoesNotReportCurrentValueFile()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingCurrent = Path.Combine(root, "current.mov");

        var presenter = new DrawablePresenter();
        presenter.Target.CurrentValue = VideoDrawable(missingCurrent);
        presenter.Target.Expression = Beutl.Engine.Expressions.Expression.CreateReference<Drawable>(Guid.NewGuid());

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(ElementWith(root, presenter));
        var hierarchyRoot = new TestHierarchicalRoot();
        hierarchyRoot.HierarchicalChildren.Add(scene);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Does.Not.Contain(missingCurrent));
    }

    // DrawableTimeController.PostUpdate renders context.Get(Target); with a broken reference the effective
    // target is DefaultValue (null), so the stale CurrentValue target's missing file must not be reported.
    [Test]
    public void CollectRenderableSources_UnresolvableTimeControllerTargetExpression_DoesNotReportCurrentValueFile()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingCurrent = Path.Combine(root, "current.mov");

        var controller = new DrawableTimeController();
        controller.Target.CurrentValue = VideoDrawable(missingCurrent);
        controller.Target.Expression = Beutl.Engine.Expressions.Expression.CreateReference<Drawable>(Guid.NewGuid());

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(ElementWith(root, controller));
        var hierarchyRoot = new TestHierarchicalRoot();
        hierarchyRoot.HierarchicalChildren.Add(scene);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Does.Not.Contain(missingCurrent));
    }

    // FilterEffectPresenter and DelayAnimationEffect render their generated resource target from the
    // effective property value; a broken reference evaluates to DefaultValue, so a missing media source
    // inside the stale CurrentValue filter must not be reported.
    [Test]
    public void CollectRenderableSources_UnresolvableFilterTargetExpression_DoesNotReportCurrentValueFile()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string presenterMissing = Path.Combine(root, "presenter-fx.mov");
        string delayMissing = Path.Combine(root, "delay-fx.mov");

        var presenter = new Beutl.Graphics.Effects.FilterEffectPresenter();
        presenter.Target.CurrentValue = FilterWithMissingSource(presenterMissing);
        presenter.Target.Expression = Beutl.Engine.Expressions.Expression.CreateReference<FilterEffect>(Guid.NewGuid());

        var delay = new Beutl.Graphics.Effects.DelayAnimationEffect();
        delay.Effect.CurrentValue = FilterWithMissingSource(delayMissing);
        delay.Effect.Expression = Beutl.Engine.Expressions.Expression.CreateReference<FilterEffect>(Guid.NewGuid());

        var drawable = new SourceVideo();
        var group = (FilterEffectGroup)drawable.FilterEffect.CurrentValue!;
        group.Children.Add(presenter);
        group.Children.Add(delay);

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(ElementWith(root, drawable));
        var hierarchyRoot = new TestHierarchicalRoot();
        hierarchyRoot.HierarchicalChildren.Add(scene);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Does.Not.Contain(presenterMissing).And.Not.Contain(delayMissing));
    }

    // SceneDrawable/SceneSound composite the effective ReferencedScene value; a broken reference evaluates
    // to DefaultValue, so missing media inside the stale CurrentValue scene must not be reported.
    [Test]
    public void CollectRenderableSources_UnresolvableReferencedSceneExpression_DoesNotReportCurrentValueMedia()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingImage = Path.Combine(root, "child.png");
        string missingSound = Path.Combine(root, "child.mp3");

        var childScene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "child.scene")) };
        childScene.Children.Add(CreateImageElement(root, missingImage));
        childScene.Children.Add(CreateSoundElement(root, missingSound));

        var sceneDrawable = new SceneDrawable();
        sceneDrawable.ReferencedScene.CurrentValue = childScene;
        sceneDrawable.ReferencedScene.Expression = Beutl.Engine.Expressions.Expression.CreateReference<Scene>(Guid.NewGuid());

        var sceneSound = new SceneSound();
        sceneSound.ReferencedScene.CurrentValue = childScene;
        sceneSound.ReferencedScene.Expression = Beutl.Engine.Expressions.Expression.CreateReference<Scene>(Guid.NewGuid());

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "root.scene")) };
        scene.Children.Add(ElementWith(root, sceneDrawable));
        scene.Children.Add(ElementWith(root, sceneSound));
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
        graphEffect.Model.CurrentValue!.Nodes.Add(node);
        return graphEffect;
    }

    // A save-frame preflight passes a zero-duration window; the referenced-scene child gate must
    // point-sample (Contains) like SortLayers, or a child active at the sampled time is wrongly skipped
    // and its missing media escapes preflight.
    [Test]
    public void CollectRenderableSources_SaveFrame_ReferencedSceneChildAtSampleTime_IsReported()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingImage = Path.Combine(root, "child.png");

        var childScene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "child.scene")) };
        Element child = CreateImageElement(root, missingImage);
        child.Start = TimeSpan.Zero;
        child.Length = TimeSpan.FromSeconds(5);
        childScene.Children.Add(child);

        var sceneDrawable = new SceneDrawable();
        sceneDrawable.ReferencedScene.CurrentValue = childScene;
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "root.scene")) };
        Element outer = ElementWith(root, sceneDrawable);
        outer.Start = TimeSpan.Zero;
        outer.Length = TimeSpan.FromSeconds(10);
        scene.Children.Add(outer);

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, TimeSpan.FromSeconds(1)));

        Assert.That(missing, Does.Contain(missingImage));
    }

    // An AudioVisualizerDrawable.Source that is a SceneSound composes the referenced scene's audio to draw
    // its waveform; the structural SceneSound audio walk never fires for a property value, so preflight
    // must dispatch it or the referenced scene's missing audio is missed.
    [Test]
    public void CollectRenderableSources_VisualizerSceneSoundSource_ReportsReferencedAudio()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingSound = Path.Combine(root, "vis.mp3");

        var childScene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "child.scene")) };
        childScene.Children.Add(CreateSoundElement(root, missingSound));

        var sceneSound = new SceneSound();
        sceneSound.ReferencedScene.CurrentValue = childScene;
        var visualizer = new Beutl.Graphics.AudioVisualizers.AudioWaveformDrawable();
        visualizer.Source.CurrentValue = sceneSound;

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "root.scene")) };
        scene.Children.Add(ElementWith(root, visualizer));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Does.Contain(missingSound));
    }

    // A DrawableBrush paints a shape with a nested Drawable that BrushConstructor renders; it is reachable
    // only as a property value, so preflight must dispatch it or a missing source inside it is missed.
    [Test]
    public void CollectRenderableSources_DrawableBrushDrawable_ReportsNestedSource()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string missingVideo = Path.Combine(root, "brush.mov");

        var brush = new DrawableBrush();
        brush.Drawable.CurrentValue = VideoDrawable(missingVideo);
        var shape = new RectShape();
        shape.Fill.CurrentValue = brush;

        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "root.scene")) };
        scene.Children.Add(ElementWith(root, shape));

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, s_wholeScene));

        Assert.That(missing, Does.Contain(missingVideo));
    }

    private static SourceSound SoundDrawable(string sourcePath)
    {
        var source = new SoundSource();
        source.ReadFrom(new Uri(sourcePath));
        var sound = new SourceSound();
        sound.Source.CurrentValue = source;
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
        drawable.Source.CurrentValue = MakeVideoSource(sourcePath);
        return drawable;
    }

    private static Element CreateVideoElement(string root, string sourcePath)
        => ElementWith(root, VideoDrawable(sourcePath));

    private static Element CreateImageElement(string root, string sourcePath)
    {
        var source = new ImageSource();
        source.ReadFrom(new Uri(sourcePath));
        var drawable = new SourceImage();
        drawable.Source.CurrentValue = source;
        return ElementWith(root, drawable);
    }

    private static Element CreateSoundElement(string root, string sourcePath)
    {
        var source = new SoundSource();
        source.ReadFrom(new Uri(sourcePath));
        var sound = new SourceSound();
        sound.Source.CurrentValue = source;
        return ElementWith(root, sound);
    }

    private static Element ElementWith(string root, Beutl.Engine.EngineObject obj)
    {
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(1),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(root, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(obj);
        return element;
    }
}
