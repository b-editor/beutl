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

    private static Element CreateVideoElement(string root, string sourcePath)
    {
        var source = new VideoSource();
        source.ReadFrom(new Uri(sourcePath));
        var drawable = new SourceVideo();
        drawable.Source.CurrentValue = source;
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(1),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(root, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(drawable);
        return element;
    }
}
