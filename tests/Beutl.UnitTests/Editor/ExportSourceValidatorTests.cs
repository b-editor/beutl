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

    // A missing image/audio original held inside a referenced scene must block export the same way a
    // missing top-level video does; otherwise the resource loader renders blank/silence and export
    // succeeds with missing content.
    [Test]
    public void GetMissingPaths_ReportsMissingImageAndSoundInReferencedScene()
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

        Assert.That(missing, Is.EquivalentTo(new[] { missingImage, missingSound }));
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

    private static Element CreateVideoElement(string root, string sourcePath)
    {
        var source = new VideoSource();
        source.ReadFrom(new Uri(sourcePath));
        var drawable = new SourceVideo();
        drawable.Source.CurrentValue = source;
        return ElementWith(root, drawable);
    }

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
