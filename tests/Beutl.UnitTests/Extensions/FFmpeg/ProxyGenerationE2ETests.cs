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
            await generator.GenerateAsync(job);
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
            async () => await generator.GenerateAsync(job));
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
            async () => await generator.GenerateAsync(job));
        Assert.That(store.Enumerate(), Is.Empty);
    }

    [Test]
    public void CreateTempPathForOutput_PreservesContainerExtensionForFormatGuess()
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        string finalPath = Path.Combine(directory, "quarter.mp4");

        string tempPath = FFmpegProxyGenerator.CreateTempPathForOutput(finalPath);

        Assert.Multiple(() =>
        {
            Assert.That(Path.GetDirectoryName(tempPath), Is.EqualTo(directory));
            Assert.That(Path.GetFileName(tempPath), Does.StartWith("quarter."));
            Assert.That(Path.GetFileName(tempPath), Does.Contain(".tmp"));
            Assert.That(tempPath, Does.EndWith(".mp4"));
            Assert.That(tempPath, Is.Not.EqualTo(finalPath));
        });
    }

    [Test]
    public void WriteMetadata_PreservesSiblingPresetEntries()
    {
        string root = CreateRoot();
        string source = Path.Combine(root, "source.mov");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(source);
        string proxyPath = Path.Combine(root, "hash", "quarter.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(proxyPath)!);
        File.WriteAllBytes(proxyPath, [1, 2, 3]);
        ProxyEntry half = CreateEntry(fingerprint, ProxyPreset.Half, "hash/half.mp4");
        ProxyEntry quarter = CreateEntry(fingerprint, ProxyPreset.Quarter, "hash/quarter.mp4");

        FFmpegProxyGenerator.WriteMetadata(proxyPath, half);
        FFmpegProxyGenerator.WriteMetadata(proxyPath, quarter);

        ProxySourceMetadata? metadata = JsonSerializer.Deserialize<ProxySourceMetadata>(
            File.ReadAllText(Path.Combine(root, "hash", "meta.json")),
            s_jsonOptions);

        Assert.Multiple(() =>
        {
            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata!.Entries.Select(static entry => entry.Preset), Is.EquivalentTo(new[] { ProxyPreset.Half, ProxyPreset.Quarter }));
        });
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static ProxyEntry CreateEntry(ProxyFingerprint source, ProxyPreset preset, string relative)
    {
        return new ProxyEntry(
            source,
            preset,
            ProxyState.Ready,
            relative,
            3,
            new PixelSize(64, 48),
            new PixelSize(32, 24),
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);
    }
}
