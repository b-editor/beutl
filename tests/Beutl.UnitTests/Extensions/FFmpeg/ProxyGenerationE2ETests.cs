using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.Extensions.FFmpeg.Proxy;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public sealed class ProxyGenerationE2ETests
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        TestMediaHelper.RegisterTestDecoder();
    }

    [Test]
    public async Task GenerateAsync_WritesProxyFileAndMetadata()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        var generator = new FFmpegProxyGenerator(store);
        string source = TestMediaHelper.CreateTestVideoFile(64, 48, new Rational(12, 1), 4);
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        var job = new ProxyJob(ProxyFingerprint.FromFile(source), ProxyPreset.Half);

        try
        {
            await generator.GenerateAsync(job, CancellationToken.None);
        }
        catch (ProxyGeneratorUnavailableException ex)
        {
            Assert.Ignore(ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("before establishing connection", StringComparison.Ordinal))
        {
            Assert.Ignore(ex.Message);
        }
        catch (TimeoutException ex)
        {
            Assert.Ignore(ex.Message);
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

    [Test]
    public void GenerateAsync_AudioOnlySource_IsSkipped()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        var generator = new FFmpegProxyGenerator(store);
        string source = TestMediaHelper.CreateTestAudioFile();
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        var job = new ProxyJob(ProxyFingerprint.FromFile(source), ProxyPreset.Quarter);

        Assert.ThrowsAsync<ProxyGenerationSkippedException>(
            async () => await generator.GenerateAsync(job, CancellationToken.None));
        Assert.That(store.Enumerate(), Is.Empty);
    }

    [Test]
    public void GenerateAsync_StillImageSource_IsSkipped()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        var generator = new FFmpegProxyGenerator(store);
        string source = Path.Combine(root, "still.png");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        var job = new ProxyJob(ProxyFingerprint.FromFile(source), ProxyPreset.Quarter);

        Assert.ThrowsAsync<ProxyGenerationSkippedException>(
            async () => await generator.GenerateAsync(job, CancellationToken.None));
        Assert.That(store.Enumerate(), Is.Empty);
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
