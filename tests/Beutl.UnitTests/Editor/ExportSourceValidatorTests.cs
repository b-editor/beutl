using Beutl.Animation;
using Beutl.Audio;
using Beutl.Editor;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
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

    // An animated Source keyframe referencing a since-replaced file must be filtered to the render
    // window: a window entirely after the switch never samples the old file, so it must not block a
    // partial-range export, while a window covering both keyframes still requires it.
    [Test]
    public void CollectRenderableSources_FiltersAnimatedSourceKeyframesToRenderWindow()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string oldMissing = Path.Combine(root, "old.mov");
        string newExisting = Path.Combine(root, "new.mov");
        File.WriteAllBytes(newExisting, [1]);

        var drawable = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.Zero, Value = MakeVideoSource(oldMissing) });
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.FromSeconds(5), Value = MakeVideoSource(newExisting) });
        drawable.Source.Animation = animation;

        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(10),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(root, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(drawable);
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        scene.Children.Add(element);

        IReadOnlyList<string> wholeRange = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(10))));
        IReadOnlyList<string> afterSwitch = ExportSourceValidator.GetMissingPaths(
            ExportSourceValidator.CollectRenderableSources(scene, new TimeRange(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(4))));

        Assert.Multiple(() =>
        {
            Assert.That(wholeRange, Is.EqualTo(new[] { oldMissing }));
            Assert.That(afterSwitch, Is.Empty);
        });
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
