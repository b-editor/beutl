using Beutl.Audio;
using Beutl.Editor;
using Beutl.Graphics;
using Beutl.Media.Source;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public sealed class ExportSourceValidatorTests
{
    [Test]
    public void GetMissingFileSources_ReturnsMissingSourcesReferencedByScene()
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

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingFileSources(scene);

        Assert.That(missing, Is.EqualTo(new[] { missingPath }));
    }

    // A missing image/audio original held inside a referenced scene must block export the same way a
    // missing top-level video does; otherwise the resource loader renders blank/silence and export
    // succeeds with missing content.
    [Test]
    public void GetMissingFileSources_ReportsMissingImageAndSoundInReferencedScene()
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

        IReadOnlyList<string> missing = ExportSourceValidator.GetMissingFileSources(scene);

        Assert.That(missing, Is.EquivalentTo(new[] { missingImage, missingSound }));
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
