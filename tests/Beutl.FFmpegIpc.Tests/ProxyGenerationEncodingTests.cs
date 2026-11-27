using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Extensions.FFmpeg;
using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.Extensions.FFmpeg.Proxy;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Pixel;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.FFmpegIpc.Tests;

// Generates a proxy end to end through the encoding worker. It lives here rather than in Beutl.UnitTests because
// this project deploys the worker next to the tests and CI runs it with the FFmpeg libraries installed.
[TestFixture]
public sealed class ProxyGenerationEncodingTests
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    [Test]
    public async Task GenerateAsync_WritesExactlyOneVideoStream()
    {
        DecodingWorkerTestHelpers.RequireDecodingWorker();
        string source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "deinterlace-offset-tff.mp4");
        string root = Directory.CreateTempSubdirectory("beutl-proxy-streams-").FullName;
        try
        {
            var decoders = new DecoderRegistry(new());
            decoders.Register(new FFmpegDecoderInfo(new FFmpegDecodingSettings()));
            var store = new ProxyStore(root);
            var generator = new FFmpegProxyGenerator(store, new Beutl.Services.NotificationService(), decoders);
            var job = new ProxyJob(ProxyFingerprint.FromFile(source), ProxyPreset.Half);

            await generator.GenerateAsync(job);

            var entry = store.TryGet(job.Source, job.Preset);
            Assert.That(entry, Is.Not.Null);
            Assert.That(Mp4TrackTable.ReadTypes(Path.Combine(root, entry!.ProxyFileRelative)), Is.EqualTo(new[] { "vide" }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("deinterlace-tff.mp4", 120, 240)]
    [TestCase("deinterlace-offset-tff.mp4", 15, 29)]
    public async Task GenerateAsync_UsesTheSameFieldGridAsLiveDecode(string fileName, int inputFrames, int outputFrames)
    {
        DecodingWorkerTestHelpers.RequireDecodingWorker();
        string source = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        string root = Path.Combine(Path.GetTempPath(), $"beutl-deinterlace-proxy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var decoders = new DecoderRegistry(new());
            decoders.Register(new FFmpegDecoderInfo(new FFmpegDecodingSettings()));
            var store = new ProxyStore(root);
            var generator = new FFmpegProxyGenerator(store, new Beutl.Services.NotificationService(), decoders);
            var fingerprint = ProxyFingerprint.FromFile(source);
            var fields = new VideoDeinterlaceOptions(DeinterlaceMode.On, DeinterlaceFieldOrder.Auto,
                DeinterlaceMethod.Bwdif, DeinterlaceFrameRateMode.Field);
            var options = new MediaOptions(MediaMode.Video) { Deinterlace = fields, PreferredProxyPreset = ProxyPreset.Half };
            try
            {
                await generator.GenerateAsync(new ProxyJob(fingerprint, ProxyPreset.Half));
                await generator.GenerateAsync(new ProxyJob(fingerprint, ProxyPreset.Half, deinterlace: fields));
            }
            catch (ProxyGeneratorUnavailableException ex)
            {
                Assert.Ignore($"FFmpeg natives unavailable ({ex.Message}); skipping.");
            }

            store = new ProxyStore(root);
            decoders.ProxyResolver = new ProxyResolver(store);
            var off = store.TryGet(fingerprint, ProxyPreset.Half)!;
            var on = store.TryGet(fingerprint, ProxyPreset.Half, fields)!;
            Assert.That(on.ProxyFileRelative, Is.Not.EqualTo(off.ProxyFileRelative));
            Assert.That(on.Deinterlace, Is.EqualTo(fields));
            Assert.That(on.DeinterlaceStatus, Is.EqualTo(DeinterlaceApplicationStatus.Applied));
            Assert.That(on.DetectedFieldOrder, Is.EqualTo(VideoFieldOrder.TopFieldFirst));

            var clip = new SourceVideo
            {
                Source = { Value = VideoSource.Open(source) },
                DeinterlaceMode = { Value = fields.Mode },
                DeinterlaceFrameRateMode = { Value = fields.FrameRateMode },
            };
            using var exportResource = clip.ToResource(new CompositionContext(MediaTime.Zero)
            {
                Decoders = decoders,
                DisableResourceShare = true,
            });
            using var previewResource = clip.ToResource(new CompositionContext(MediaTime.Zero)
            {
                Decoders = decoders,
                PreferProxy = true,
                PreferredProxyPreset = ProxyPreset.Half,
            });
            var live = exportResource.Source!.MediaReader!;
            var preview = previewResource.Source!.MediaReader!;
            using var encoded = MediaReader.Open(Path.Combine(root, on.ProxyFileRelative), new MediaOptions(MediaMode.Video), decoders);
            using var unfilteredPreview = MediaReader.Open(source, options with { Deinterlace = default, PreferProxy = true }, decoders);
            Assert.Multiple(() =>
            {
                Assert.That(preview.ProxyResolution?.Deinterlace, Is.EqualTo(fields));
                Assert.That(preview.VideoInfo.FrameRate, Is.EqualTo(new Rational(60000, 1001)));
                Assert.That(preview.VideoInfo.NumFrames, Is.EqualTo(live.VideoInfo.NumFrames).And.EqualTo(outputFrames));
                Assert.That(preview.VideoInfo.Duration, Is.EqualTo(live.VideoInfo.Duration));
                Assert.That(preview.VideoInfo.DetectedFieldOrder, Is.EqualTo(VideoFieldOrder.TopFieldFirst));
                Assert.That(preview.VideoInfo.DeinterlaceStatus, Is.EqualTo(DeinterlaceApplicationStatus.Applied));
                Assert.That(encoded.VideoInfo.DetectedFieldOrder, Is.EqualTo(VideoFieldOrder.Progressive));
                Assert.That(encoded.VideoInfo.FrameRate, Is.EqualTo(preview.VideoInfo.FrameRate));
                Assert.That(unfilteredPreview.ProxyResolution?.Deinterlace, Is.EqualTo(VideoDeinterlaceOptions.Default));
                Assert.That(unfilteredPreview.VideoInfo.NumFrames, Is.EqualTo(inputFrames));
            });

            var justBeforeEnd = exportResource.Source.Duration - new MediaTime(1);
            Assert.That(exportResource.Source.GetFrameIndex(justBeforeEnd), Is.EqualTo(outputFrames - 1));
            Assert.That(previewResource.Source.GetFrameIndex(justBeforeEnd), Is.EqualTo(outputFrames - 1));

            foreach (int frame in new[] { 0, 1, 17, 18, outputFrames / 2, outputFrames / 2 + 1, outputFrames - 2, outputFrames - 1 })
            {
                Assert.That(BarCenter(preview, frame), Is.EqualTo(BarCenter(live, frame)).Within(0.01),
                    $"proxy field {frame} must show the same point in the motion as the original");
                Assert.That(BarCenter(preview, frame), Is.EqualTo(BarCenter(encoded, frame)),
                    $"proxy field {frame} must be read directly from the progressive proxy");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static double BarCenter(MediaReader reader, int frame)
    {
        Assert.That(reader.ReadVideo(frame, out var image), Is.True, $"frame {frame}");
        using (image)
        {
            var bitmap = image!.Value;
            var pixels = bitmap.GetPixelSpan();
            int total = 0;
            int weighted = 0;
            for (int row = 0; row < bitmap.Height; row++)
            {
                for (int column = 0; column < bitmap.Width; column++)
                {
                    if (pixels[row * bitmap.RowBytes + column * 4] < 128) continue;
                    total++;
                    weighted += column;
                }
            }
            Assert.That(total, Is.Positive);
            return weighted / ((double)total * bitmap.Width);
        }
    }

    [TestCase(BitmapColorTransfer.Bt709, BitmapColorPrimaries.Bt709)]
    [TestCase(BitmapColorTransfer.Srgb, BitmapColorPrimaries.Bt709)]
    [TestCase(BitmapColorTransfer.Unknown, BitmapColorPrimaries.Unknown)]
    [TestCase(BitmapColorTransfer.Bt709, BitmapColorPrimaries.Smpte170M)]
    public async Task GenerateAsync_PreservesOriginalTagsAndTransferInterpretation(
        BitmapColorTransfer transfer, BitmapColorPrimaries primaries)
    {
        if (!FFmpegWorkerProcess.IsWorkerAvailable(AppContext.BaseDirectory))
            Assert.Ignore("FFmpeg worker binary not present in the test output; skipping.");

        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var tags = new VideoColorTags(transfer, primaries, VideoColorMatrix.Bt709, VideoColorRange.Limited);
            var decoders = new DecoderRegistry(new());
            decoders.Register(new SyntheticVideoDecoderInfo(tags));
            decoders.Register(new FFmpegDecoderInfo(new FFmpegDecodingSettings()));
            var store = new ProxyStore(root);
            decoders.ProxyResolver = new ProxyResolver(store);
            var generator = new FFmpegProxyGenerator(store, new Beutl.Services.NotificationService(), decoders);
            // The decoder below opens any file with this extension as 4 frames of 64x48 at 12 fps. The fingerprint
            // reads the file, so it must not be empty.
            string source = Path.Combine(root, "source" + SyntheticVideoDecoderInfo.Extension);
            File.WriteAllBytes(source, [1, 2, 3, 4]);
            var job = new ProxyJob(ProxyFingerprint.FromFile(source), ProxyPreset.Half);

            try
            {
                await generator.GenerateAsync(job);
            }
            catch (ProxyGeneratorUnavailableException ex)
            {
                Assert.Ignore($"FFmpeg natives unavailable ({ex.Message}); skipping.");
            }

            ProxyEntry? entry = store.TryGet(job.Source, ProxyPreset.Half);
            Assert.That(entry, Is.Not.Null);
            string proxyPath = Path.Combine(root, entry!.ProxyFileRelative.Replace('/', Path.DirectorySeparatorChar));
            string metadataPath = Path.Combine(Path.GetDirectoryName(proxyPath)!, "meta.json");
            ProxySourceMetadata? metadata = JsonSerializer.Deserialize<ProxySourceMetadata>(
                File.ReadAllText(metadataPath),
                s_jsonOptions);

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(proxyPath), Is.True);
                Assert.That(new FileInfo(proxyPath).Length, Is.GreaterThan(0));
                Assert.That(entry.State, Is.EqualTo(ProxyState.Ready));
                Assert.That(entry.OriginalLogicalFrameSize, Is.EqualTo(new PixelSize(64, 48)));
                Assert.That(entry.ProxyDecodedFrameSize.Width, Is.LessThanOrEqualTo(64));
                Assert.That(entry.ProxyDecodedFrameSize.Height, Is.LessThanOrEqualTo(48));
                Assert.That(entry.OriginalColorTags, Is.EqualTo(tags));
                Assert.That(metadata, Is.Not.Null);
                Assert.That(metadata!.Source, Is.EqualTo(job.Source));
                Assert.That(metadata.Entries.Single(), Is.EqualTo(entry));
            });

            VideoSource video = VideoSource.Open(source);
            var context = new CompositionContext(MediaTime.Zero) { Decoders = decoders, DisableResourceShare = true };
            using var original = video.ToResource(context);
            context.PreferProxy = true;
            context.PreferredProxyPreset = ProxyPreset.Half;
            using var proxy = video.ToResource(context);
            Assert.That(proxy.ProxyResolution, Is.Not.Null, "The parity check must draw the generated proxy.");
            Assert.That(proxy.OriginalColorTags, Is.EqualTo(tags));
            Assert.That(proxy.MediaReader!.VideoInfo.ColorTags, Is.EqualTo(
                new VideoColorTags(BitmapColorTransfer.Bt709, BitmapColorPrimaries.Bt709, VideoColorMatrix.Bt709, VideoColorRange.Limited)));

            foreach (VideoTransferInterpretation interpretation in Enum.GetValues<VideoTransferInterpretation>())
            {
                // Lossy H.264, limited-range YUV and chroma subsampling can shift a patch by a few 8-bit codes.
                Assert.That(Draw(proxy, interpretation), Is.EqualTo(Draw(original, interpretation)).Within(4),
                    $"Original and proxy must agree for {interpretation}.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static int[] Draw(VideoSource.Resource resource, VideoTransferInterpretation interpretation)
    {
        using var target = new CpuTarget(resource.LogicalFrameSize);
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Preview))
        {
            canvas.DrawVideoSource(resource, 0, Brushes.Resource.White, null, interpretation);
        }

        using Bitmap snapshot = target.Snapshot();
        using Bitmap srgb = snapshot.Convert(BitmapColorType.Bgra8888, colorSpace: BitmapColorSpace.Srgb);
        Span<Bgra8888> row = srgb.GetRow<Bgra8888>(srgb.Height / 2);
        return [row[8].G, row[32].G, row[56].R, row[56].G, row[56].B];
    }

    private sealed class CpuTarget(PixelSize size) : RenderTarget(
        SKSurface.Create(new SKImageInfo(size.Width, size.Height, SKColorType.RgbaF16, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear())),
        size.Width, size.Height);

    private sealed class SyntheticVideoDecoderInfo(VideoColorTags tags) : IDecoderInfo
    {
        public const string Extension = ".proxysynthvideo";

        public string Name => "Synthetic proxy source";

        public MediaReader? Open(string file, MediaOptions options)
        {
            return Path.GetExtension(file).Equals(Extension, StringComparison.OrdinalIgnoreCase)
                ? new SyntheticVideoReader(new PixelSize(64, 48), new Rational(12, 1), 4, tags)
                : null;
        }

        public IEnumerable<string> VideoExtensions() => [Extension];

        public IEnumerable<string> AudioExtensions() => [];
    }

    private sealed class SyntheticVideoReader(PixelSize frameSize, Rational frameRate, int frameCount, VideoColorTags tags) : MediaReader
    {
        private readonly BitmapColorSpace _colorSpace = BitmapColorSpaceMapping.BuildTargetColorSpace(tags.Transfer, tags.Primaries);

        public override VideoStreamInfo VideoInfo { get; } = new("synthetic", frameCount, frameSize, frameRate)
        {
            ColorTags = tags,
        };

        public override AudioStreamInfo AudioInfo { get; } = new("synthetic", Rational.Zero, 44100, 2);

        public override bool HasVideo => true;

        public override bool HasAudio => false;

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            if (frame < 0 || frame >= frameCount)
            {
                image = null;
                return false;
            }

            var bitmap = new Bitmap(frameSize.Width, frameSize.Height, colorSpace: _colorSpace);
            for (int y = 0; y < bitmap.Height; y++)
            {
                Span<Bgra8888> row = bitmap.GetRow<Bgra8888>(y);
                row[..16].Fill(new Bgra8888(30, 30, 30, 255));
                row[16..48].Fill(new Bgra8888(128, 128, 128, 255));
                row[48..].Fill(new Bgra8888(144, 112, 80, 255));
            }

            image = Ref<Bitmap>.Create(bitmap);
            return true;
        }

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            sound = null;
            return false;
        }
    }
}
