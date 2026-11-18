using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.Audio;
using Beutl.Extensions.FFmpeg;
using Beutl.Extensions.FFmpeg.Proxy;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Pixel;
using Beutl.Media.Proxy;
using Beutl.Media.Source;

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

    private readonly SyntheticVideoDecoderInfo _decoder = new();

    [OneTimeSetUp]
    public void OneTimeSetUp() => DecoderRegistry.Register(_decoder);

    [OneTimeTearDown]
    public void OneTimeTearDown() => DecoderRegistry.Unregister(_decoder);

    [Test]
    public async Task GenerateAsync_WritesProxyFileAndMetadata()
    {
        if (!FFmpegWorkerProcess.IsWorkerAvailable(AppContext.BaseDirectory))
            Assert.Ignore("FFmpeg worker binary not present in the test output; skipping.");

        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ProxyStore(root);
            var generator = new FFmpegProxyGenerator(store, new Beutl.Services.NotificationService());
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
                Assert.That(metadata, Is.Not.Null);
                Assert.That(metadata!.Source, Is.EqualTo(job.Source));
                Assert.That(metadata.Entries.Single(), Is.EqualTo(entry));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class SyntheticVideoDecoderInfo : IDecoderInfo
    {
        public const string Extension = ".proxysynthvideo";

        public string Name => "Synthetic proxy source";

        public MediaReader? Open(string file, MediaOptions options)
        {
            return Path.GetExtension(file).Equals(Extension, StringComparison.OrdinalIgnoreCase)
                ? new SyntheticVideoReader(new PixelSize(64, 48), new Rational(12, 1), 4)
                : null;
        }

        public IEnumerable<string> VideoExtensions() => [Extension];

        public IEnumerable<string> AudioExtensions() => [];
    }

    private sealed class SyntheticVideoReader(PixelSize frameSize, Rational frameRate, int frameCount) : MediaReader
    {
        public override VideoStreamInfo VideoInfo { get; } = new("synthetic", frameCount, frameSize, frameRate);

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

            var bitmap = new Bitmap(frameSize.Width, frameSize.Height);
            byte value = (byte)(frame * 10 % 256);
            bitmap.GetPixelSpan<Bgra8888>().Fill(new Bgra8888(value, value, value, 255));
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
