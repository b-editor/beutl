using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Decoding;

namespace Beutl.E2ETests.Scenarios;

// Avalonia's Bitmap needs a platform render interface, so the thumbnail path is exercised here
// rather than in Beutl.UnitTests. BEUTL_HOME is redirected by AssemblySetUp, so the templates
// directory these write into is a throwaway one.
[TestFixture]
[NonParallelizable]
public class FileBrowserTemplateThumbnailTests
{
    [Test]
    public void IsObjectTemplateFile_OnlyMatchesJsonUnderTheTemplatesDirectory()
    {
        FileThumbnailService service = FileThumbnailService.Instance;
        string templates = ObjectTemplateService.Instance.DirectoryPath;

        Assert.Multiple(() =>
        {
            Assert.That(service.IsObjectTemplateFile(Path.Combine(templates, "a.json")), Is.True);
            Assert.That(service.IsObjectTemplateFile(Path.Combine(templates, "pkg", "a.json")), Is.True);
            Assert.That(service.IsObjectTemplateFile(Path.Combine(templates, "a.png")), Is.False);
            Assert.That(
                service.IsObjectTemplateFile(Path.Combine(Path.GetTempPath(), "elsewhere.json")),
                Is.False);
        });
    }

    // This harness registers no decoders, so the video/audio extensions come from a fake one here.
    [Test]
    public void CanGenerateThumbnail_CoversImagesVideosAndTemplates()
    {
        FileThumbnailService service = FileThumbnailService.Instance;
        string templates = ObjectTemplateService.Instance.DirectoryPath;
        var decoder = new FakeDecoderInfo();
        DecoderRegistry.Register(decoder);

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(service.CanGenerateThumbnail("a.png"), Is.True);
                Assert.That(service.CanGenerateThumbnail("a.fakevid"), Is.True);
                Assert.That(service.CanGenerateThumbnail(Path.Combine(templates, "a.json")), Is.True);
                Assert.That(service.CanGenerateThumbnail("a.fakeaud"), Is.False);
                Assert.That(service.CanGenerateThumbnail("a.txt"), Is.False);
            });
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
        }
    }

    [AvaloniaTest]
    public async Task GetThumbnailAsync_ReadsTheEmbeddedPreviewOfATemplate()
    {
        var shape = new RectShape
        {
            Width = { CurrentValue = 100 },
            Height = { CurrentValue = 100 },
            Fill = { CurrentValue = new SolidColorBrush(Colors.Red) }
        };

        ObjectTemplateItem? item = await ObjectTemplateService.Instance
            .AddFromInstanceAsync(shape, $"thumb-{Guid.NewGuid():N}");

        Assert.That(item?.Preview, Is.Not.Null.And.Not.Empty);

        var thumbnail = await FileThumbnailService.Instance.GetThumbnailAsync(item!.FilePath!);

        Assert.That(thumbnail, Is.Not.Null);
        Assert.That(thumbnail!.PixelSize.Width, Is.GreaterThan(0));
    }

    [AvaloniaTest]
    public async Task GetThumbnailAsync_TemplateWithoutAPreview_ReturnsNull()
    {
        ObjectTemplateItem? item = await ObjectTemplateService.Instance
            .AddFromInstanceAsync(new Audio.Effects.AudioEffectGroup(), $"nothumb-{Guid.NewGuid():N}");

        Assert.That(item?.Preview, Is.Null);

        var thumbnail = await FileThumbnailService.Instance.GetThumbnailAsync(item!.FilePath!);

        Assert.That(thumbnail, Is.Null);
    }

    private sealed class FakeDecoderInfo : IDecoderInfo
    {
        public string Name => "Fake Decoder";

        public MediaReader? Open(string file, MediaOptions options) => null;

        public IEnumerable<string> VideoExtensions() => [".fakevid"];

        public IEnumerable<string> AudioExtensions() => [".fakeaud"];
    }
}
