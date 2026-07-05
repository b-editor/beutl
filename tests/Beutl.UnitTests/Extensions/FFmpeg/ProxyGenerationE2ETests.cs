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

    [Test]
    public void FinalizeAsync_WhenRegisterKeepsThrowing_KeepsProxyFileAndSurfacesError()
    {
        string root = CreateRoot();
        var store = new CountingStore(root, failuresBeforeSuccess: int.MaxValue);
        var generator = new FFmpegProxyGenerator(store);
        (string finalPath, ProxyEntry entry) = SeedFinalizedArtifact(root);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await generator.FinalizeAsync(finalPath, entry));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(finalPath), Is.True, "a finalized, valid proxy artifact must never be deleted on a Register failure");
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(finalPath)!, "meta.json")), Is.True);
            Assert.That(store.RegisterAttempts, Is.EqualTo(3), "Register must be retried before giving up");
        });
    }

    [Test]
    public async Task FinalizeAsync_RetriesTransientRegisterFailureThenSucceeds()
    {
        string root = CreateRoot();
        var store = new CountingStore(root, failuresBeforeSuccess: 2);
        var generator = new FFmpegProxyGenerator(store);
        (string finalPath, ProxyEntry entry) = SeedFinalizedArtifact(root);

        await generator.FinalizeAsync(finalPath, entry);

        Assert.Multiple(() =>
        {
            Assert.That(store.RegisterAttempts, Is.EqualTo(3));
            Assert.That(store.LastRegistered, Is.EqualTo(entry));
            Assert.That(File.Exists(finalPath), Is.True);
        });
    }

    [Test]
    public async Task FinalizeAsync_WhenSidecarWriteFails_StillRegistersEntry()
    {
        string root = CreateRoot();
        var store = new CountingStore(root, failuresBeforeSuccess: 0);
        var generator = new FFmpegProxyGenerator(store);
        (string finalPath, ProxyEntry entry) = SeedFinalizedArtifact(root);

        // Occupy the sidecar path with a directory so WriteMetadata's File.WriteAllText throws; the
        // ready artifact must still be indexed rather than left orphaned.
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(finalPath)!, "meta.json"));

        await generator.FinalizeAsync(finalPath, entry);

        Assert.Multiple(() =>
        {
            Assert.That(store.LastRegistered, Is.EqualTo(entry));
            Assert.That(File.Exists(finalPath), Is.True);
        });
    }

    [Test]
    public void PublishAsync_CanceledToken_DoesNotMoveOrRegister()
    {
        string root = CreateRoot();
        var store = new CountingStore(root, failuresBeforeSuccess: 0);
        var generator = new FFmpegProxyGenerator(store);
        string source = Path.Combine(root, "src.mov");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(source);
        string tempPath = Path.Combine(root, "tmp.mov");
        string finalPath = Path.Combine(root, "hash", "quarter.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        File.WriteAllBytes(tempPath, [9, 9, 9, 9, 9]);
        var job = new ProxyJob(fingerprint, ProxyPreset.Quarter);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await generator.PublishAsync(tempPath, finalPath, job, "hash/quarter.mp4", new PixelSize(64, 48), new PixelSize(32, 24), cts.Token));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(finalPath), Is.False, "a canceled job must not move the proxy into place");
            Assert.That(File.Exists(tempPath), Is.True, "the temp artifact remains for the caller's catch to clean up");
            Assert.That(store.RegisterAttempts, Is.EqualTo(0), "a canceled job must not register the proxy");
            Assert.That(store.LastRegistered, Is.Null);
        });
    }

    [Test]
    [TestCase(1920, 1080)]
    [TestCase(1998, 1080)]
    [TestCase(1280, 720)]
    [TestCase(720, 1280)]
    [TestCase(1440, 1080)]
    [TestCase(4096, 2160)]
    [TestCase(4000, 2000)]
    public void CalculateProxySize_KeepsAspectRatioCloseToSource(int width, int height)
    {
        var source = new PixelSize(width, height);

        PixelSize proxy = FFmpegProxyGenerator.CalculateProxySize(source, ProxyPreset.Half);

        double sourceAspect = (double)width / height;
        double proxyAspect = (double)proxy.Width / proxy.Height;

        Assert.Multiple(() =>
        {
            Assert.That(proxy.Width % 2, Is.EqualTo(0));
            Assert.That(proxy.Height % 2, Is.EqualTo(0));
            Assert.That(proxy.Width, Is.GreaterThanOrEqualTo(2));
            Assert.That(proxy.Height, Is.GreaterThanOrEqualTo(2));
            Assert.That(Math.Abs(proxyAspect - sourceAspect), Is.LessThan(sourceAspect * 0.02));
        });
    }

    private static (string FinalPath, ProxyEntry Entry) SeedFinalizedArtifact(string root)
    {
        string source = Path.Combine(root, "src.mov");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(source);
        string finalDir = Path.Combine(root, "hash");
        Directory.CreateDirectory(finalDir);
        string finalPath = Path.Combine(finalDir, "quarter.mp4");
        File.WriteAllBytes(finalPath, [9, 9, 9, 9, 9]);
        ProxyEntry entry = CreateEntry(fingerprint, ProxyPreset.Quarter, "hash/quarter.mp4");
        return (finalPath, entry);
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

    private sealed class CountingStore(string root, int failuresBeforeSuccess) : IProxyStore
    {
        public int RegisterAttempts { get; private set; }

        public ProxyEntry? LastRegistered { get; private set; }

        public string StoreRootPath => root;

        public ProxyEntry? TryGet(ProxyFingerprint source, ProxyPreset preset) => null;

        public IReadOnlyList<ProxyEntry> Enumerate() => [];

        public void Register(ProxyEntry entry)
        {
            RegisterAttempts++;
            if (RegisterAttempts <= failuresBeforeSuccess)
                throw new InvalidOperationException("index locked");

            LastRegistered = entry;
        }

        public bool TryTransition(ProxyFingerprint source, ProxyPreset preset, ProxyState newState, string? failureReason = null) => false;

        public bool Delete(ProxyFingerprint source, ProxyPreset preset) => false;

        public void Touch(ProxyFingerprint source, ProxyPreset preset, DateTime nowUtc)
        {
        }

        public long GetTotalBytes() => 0;

        public long GetTotalBytes(IReadOnlySet<string> sourceAbsolutePaths) => 0;

        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ReconcileAsync(CancellationToken cancellationToken) => Task.CompletedTask;

#pragma warning disable CS0067 // Not exercised by these tests.
        public event EventHandler<ProxyStoreChangedEventArgs>? Changed;
#pragma warning restore CS0067
    }
}
