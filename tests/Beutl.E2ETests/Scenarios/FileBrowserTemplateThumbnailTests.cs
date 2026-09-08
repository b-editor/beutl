using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Decoding;
using SkiaSharp;

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

    // A decoder can claim one extension as both kinds — AVFoundation and Media Foundation do for
    // '.adts' — so the classification alone cannot decide which stream to ask for.
    [AvaloniaTest]
    public async Task GetMediaInfoAsync_FallsBackToTheOtherStreamKind()
    {
        var decoder = new RecordingDecoderInfo();
        DecoderRegistry.Register(decoder);
        try
        {
            string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.bothkinds");
            await File.WriteAllBytesAsync(path, [0]);
            try
            {
                await FileThumbnailService.Instance.GetMediaInfoAsync(path);
            }
            finally
            {
                File.Delete(path);
            }

            // Classified video (video wins an ambiguous claim), then audio once that open fails.
            Assert.That(decoder.RequestedModes, Is.EqualTo(new[] { MediaMode.Video, MediaMode.Audio }));
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
        }
    }

    // A package preview may be far larger than the 64px the browser draws, and every listed item
    // holds its thumbnail strongly.
    [AvaloniaTest]
    public async Task GetThumbnailAsync_DownscalesAnOversizedTemplatePreview()
    {
        ObjectTemplateItem? item = await ObjectTemplateService.Instance
            .AddFromInstanceAsync(CreateRedRect(), $"large-{Guid.NewGuid():N}");
        Assert.That(item?.Preview, Is.Not.Null);

        await File.WriteAllTextAsync(item!.FilePath!, BuildTemplateJsonWithPreview(item, 1024, 1024));
        FileThumbnailService.Instance.ClearCache();

        var thumbnail = await FileThumbnailService.Instance.GetThumbnailAsync(item.FilePath!);

        Assert.That(thumbnail, Is.Not.Null);
        Assert.That(thumbnail!.PixelSize.Width, Is.LessThanOrEqualTo(FileThumbnailService.Instance.ThumbnailSize));
        Assert.That(thumbnail.PixelSize.Height, Is.LessThanOrEqualTo(FileThumbnailService.Instance.ThumbnailSize));
    }

    private static RectShape CreateRedRect()
    {
        return new RectShape
        {
            Width = { CurrentValue = 100 },
            Height = { CurrentValue = 100 },
            Fill = { CurrentValue = new SolidColorBrush(Colors.Red) }
        };
    }

    private static string BuildTemplateJsonWithPreview(ObjectTemplateItem item, int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Red);
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 90);

        JsonObject json = ObjectTemplateItem.ToJson(item).AsObject();
        json["Preview"] = Convert.ToBase64String(data.ToArray());
        return json.ToJsonString();
    }

    private sealed class FakeDecoderInfo : IDecoderInfo
    {
        public string Name => "Fake Decoder";

        public MediaReader? Open(string file, MediaOptions options) => null;

        public IEnumerable<string> VideoExtensions() => [".fakevid"];

        public IEnumerable<string> AudioExtensions() => [".fakeaud"];
    }

    private sealed class RecordingDecoderInfo : IDecoderInfo
    {
        public List<MediaMode> RequestedModes { get; } = [];

        public string Name => "Recording Decoder";

        public MediaReader? Open(string file, MediaOptions options)
        {
            RequestedModes.Add(options.StreamsToLoad);
            return null;
        }

        public IEnumerable<string> VideoExtensions() => [".bothkinds"];

        public IEnumerable<string> AudioExtensions() => [".bothkinds"];
    }
}
