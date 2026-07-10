using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.Extensions.FFmpeg.Proxy;
using Beutl.FFmpegIpc;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public sealed class FFmpegProxyGeneratorPublishTests
{
    // Fix #4: a hard-coded H.264 level (4.0) rejects legal high-FPS proxies — a 4K60 source encodes a
    // 1080p60 proxy that exceeds level 4.0's macroblock rate. No level is set so libx264 picks one.
    [Test]
    public void Configure_DoesNotForceH264Level()
    {
        var controller = new FFmpegEncodingControllerProxy(
            Path.Combine(CreateRoot(), "proxy.mp4"), new FFmpegEncodingSettings());
        var videoInfo = new VideoStreamInfo("h264", numFrames: 60, new PixelSize(3840, 2160), new Rational(60, 1));

        FFmpegProxyGenerator.Configure(controller, videoInfo, new PixelSize(1920, 1080), ProxyPreset.Half);

        Assert.That(controller.VideoSettings.Options.Any(o => o.Name == "level"), Is.False);
    }

    // An animated APNG/GIF/WebP is exposed as multi-frame video by the animated-image readers, so the
    // extension guard must not reject it before opening a reader; only genuinely single-frame formats
    // (jpg/bmp/tiff) are skipped up front, and stillness for the animatable containers is decided later
    // by frame count.
    [TestCase("clip.gif", false, true)]
    [TestCase("clip.webp", false, true)]
    [TestCase("clip.png", false, true)]
    [TestCase("clip.apng", false, true)]
    [TestCase("photo.jpg", true, false)]
    [TestCase("photo.jpeg", true, false)]
    [TestCase("photo.bmp", true, false)]
    [TestCase("photo.tif", true, false)]
    [TestCase("photo.tiff", true, false)]
    [TestCase("movie.mov", false, false)]
    [TestCase("movie.mp4", false, false)]
    public void ImageClassification_SplitsAlwaysStillFromAnimatable(string fileName, bool alwaysStill, bool animatable)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FFmpegProxyGenerator.IsAlwaysStillImage(fileName), Is.EqualTo(alwaysStill));
            Assert.That(FFmpegProxyGenerator.IsAnimatableImage(fileName), Is.EqualTo(animatable));
        });
    }

    [Test]
    public async Task MoveWithRetryAsync_RetriesTransientFailuresThenSucceeds()
    {
        string root = CreateRoot();
        string source = Path.Combine(root, "src.mp4");
        string dest = Path.Combine(root, "dst.mp4");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        int attempts = 0;

        await FFmpegProxyGenerator.MoveWithRetryAsync(
            source,
            dest,
            CancellationToken.None,
            moveAttempt: (s, d) =>
            {
                attempts++;
                if (attempts < 3)
                    return false;
                File.Move(s, d, overwrite: true);
                return true;
            },
            maxAttempts: 5,
            retryDelay: TimeSpan.FromMilliseconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(3), "the first two transient failures must be retried");
            Assert.That(File.Exists(dest), Is.True);
            Assert.That(File.Exists(source), Is.False);
        });
    }

    [Test]
    public void MoveWithRetryAsync_WhenAlwaysFails_ThrowsIOExceptionAfterMaxAttempts()
    {
        string root = CreateRoot();
        string source = Path.Combine(root, "src.mp4");
        string dest = Path.Combine(root, "dst.mp4");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        int attempts = 0;

        Assert.ThrowsAsync<IOException>(async () =>
            await FFmpegProxyGenerator.MoveWithRetryAsync(
                source,
                dest,
                CancellationToken.None,
                moveAttempt: (_, _) =>
                {
                    attempts++;
                    return false;
                },
                maxAttempts: 3,
                retryDelay: TimeSpan.FromMilliseconds(1)));

        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(3), "must give up after maxAttempts");
            Assert.That(File.Exists(source), Is.True, "source must remain when the move never succeeds");
            Assert.That(File.Exists(dest), Is.False);
        });
    }

    [Test]
    public void MoveWithRetryAsync_WhenDelegateThrowsIOException_RetriesAndRethrowsLastError()
    {
        string root = CreateRoot();
        string source = Path.Combine(root, "src.mp4");
        string dest = Path.Combine(root, "dst.mp4");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        int attempts = 0;
        IOException thrown0 = new("share violation 0");
        IOException thrown1 = new("share violation 1");

        IOException ex = (IOException)Assert.ThrowsAsync<IOException>(async () =>
            await FFmpegProxyGenerator.MoveWithRetryAsync(
                source,
                dest,
                CancellationToken.None,
                moveAttempt: (_, _) =>
                {
                    attempts++;
                    throw attempts == 1 ? thrown0 : thrown1;
                },
                maxAttempts: 2,
                retryDelay: TimeSpan.FromMilliseconds(1)))!;

        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(ex, Is.SameAs(thrown1), "the last thrown IOException must be rethrown after exhausting attempts");
            Assert.That(File.Exists(source), Is.True);
        });
    }

    [Test]
    public void MoveWithRetryAsync_PropagatesOperationCanceledExceptionFromDelegate()
    {
        string root = CreateRoot();
        string source = Path.Combine(root, "src.mp4");
        string dest = Path.Combine(root, "dst.mp4");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        using var cts = new CancellationTokenSource();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await FFmpegProxyGenerator.MoveWithRetryAsync(
                source,
                dest,
                cts.Token,
                moveAttempt: (_, _) => throw new OperationCanceledException(cts.Token)));
    }

    [Test]
    public void MoveWithRetryAsync_RespectsCancellationDuringRetryDelay()
    {
        string root = CreateRoot();
        string source = Path.Combine(root, "src.mp4");
        string dest = Path.Combine(root, "dst.mp4");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        using var cts = new CancellationTokenSource();
        int attempts = 0;

        // Task.Delay(delay, ct) throws TaskCanceledException (a derived OperationCanceledException),
        // so accept the base type or any derived type.
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await FFmpegProxyGenerator.MoveWithRetryAsync(
                source,
                dest,
                cts.Token,
                moveAttempt: (_, _) =>
                {
                    attempts++;
                    cts.Cancel();
                    return false;
                },
                maxAttempts: 5,
                retryDelay: TimeSpan.FromMilliseconds(50)));

        Assert.That(attempts, Is.EqualTo(1), "the retry delay after the first failure must observe the canceled token and stop");
    }

    [Test]
    public void MoveWithRetryAsync_PreCanceledToken_ThrowsBeforeFirstAttempt()
    {
        string root = CreateRoot();
        string source = Path.Combine(root, "src.mp4");
        string dest = Path.Combine(root, "dst.mp4");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        int attempts = 0;

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await FFmpegProxyGenerator.MoveWithRetryAsync(
                source,
                dest,
                cts.Token,
                moveAttempt: (_, _) =>
                {
                    attempts++;
                    return true;
                }));

        Assert.That(attempts, Is.EqualTo(0), "a pre-canceled token must fail fast before invoking the delegate");
    }

    [Test]
    public void PublishAsync_CancelDuringMove_DeletesMovedArtifactAndDoesNotRegister()
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

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await generator.PublishAsync(
                tempPath,
                finalPath,
                job,
                "hash/quarter.mp4",
                new PixelSize(64, 48),
                new PixelSize(32, 24),
                cts.Token,
                moveAttempt: (s, d) =>
                {
                    File.Move(s, d, overwrite: true);
                    cts.Cancel();
                    return true;
                }));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(finalPath), Is.False, "the moved artifact must be cleaned up so it is not left as an orphan claiming to be the proxy");
            Assert.That(store.RegisterAttempts, Is.EqualTo(0), "a cancel after the move must not register the proxy");
            Assert.That(store.LastRegistered, Is.Null);
        });
    }

    [Test]
    public void PublishAsync_RetriesTransientMoveFailureThenRegisters()
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
        int attempts = 0;

        Assert.DoesNotThrowAsync(async () =>
            await generator.PublishAsync(
                tempPath,
                finalPath,
                job,
                "hash/quarter.mp4",
                new PixelSize(64, 48),
                new PixelSize(32, 24),
                CancellationToken.None,
                moveAttempt: (s, d) =>
                {
                    attempts++;
                    if (attempts < 2)
                        return false;
                    File.Move(s, d, overwrite: true);
                    return true;
                }));

        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(2), "the transient move failure must be retried");
            Assert.That(File.Exists(finalPath), Is.True);
            Assert.That(store.RegisterAttempts, Is.EqualTo(1), "the proxy must be registered once the move succeeds");
            Assert.That(store.LastRegistered, Is.Not.Null);
        });
    }

    [Test]
    public async Task MoveExistingFileToBackupWithRetryAsync_StampsBackupWithRecentWriteTime()
    {
        string root = CreateRoot();
        string proxy = Path.Combine(root, "quarter.mp4");
        File.WriteAllBytes(proxy, [1, 2, 3]);
        // The proxy was generated long ago; a plain move preserves that old mtime, which reconcile's
        // age-based orphan cleanup would then treat as immediately reclaimable while a regenerate still
        // needs the backup for rollback.
        File.SetLastWriteTimeUtc(proxy, DateTime.UtcNow.AddHours(-48));

        string? backup = await FFmpegProxyGenerator.MoveExistingFileToBackupWithRetryAsync(proxy, CancellationToken.None);

        Assert.That(backup, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(backup!), Is.True);
            Assert.That(
                File.GetLastWriteTimeUtc(backup!),
                Is.GreaterThan(DateTime.UtcNow.AddHours(-1)),
                "the backup must be stamped to now so reconcile does not reclaim a live rollback backup");
        });
    }

    [Test]
    public void PublishAsync_RetriesTransientBackupMoveFailureThenRegisters()
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
        // A previous proxy exists, so the pre-move backup step runs; simulate a preview reader
        // holding it open with a transient sharing violation on the first backup attempt.
        File.WriteAllBytes(finalPath, [7, 7, 7]);
        File.WriteAllBytes(tempPath, [9, 9, 9, 9, 9]);
        var job = new ProxyJob(fingerprint, ProxyPreset.Quarter);
        int backupAttempts = 0;

        Assert.DoesNotThrowAsync(async () =>
            await generator.PublishAsync(
                tempPath,
                finalPath,
                job,
                "hash/quarter.mp4",
                new PixelSize(64, 48),
                new PixelSize(32, 24),
                CancellationToken.None,
                backupMoveAttempt: (s, d) =>
                {
                    backupAttempts++;
                    if (backupAttempts < 2)
                        return false;
                    File.Move(s, d, overwrite: false);
                    return true;
                }));

        Assert.Multiple(() =>
        {
            Assert.That(backupAttempts, Is.EqualTo(2), "the transient backup-move failure must be retried");
            Assert.That(File.Exists(finalPath), Is.True);
            Assert.That(store.RegisterAttempts, Is.EqualTo(1));
        });
    }

    [Test]
    public void PublishAsync_WhenRollbackMetadataRestoreFails_PreservesPrimaryExceptionAndRestoresFinal()
    {
        string root = CreateRoot();
        // Register always throws, so FinalizeAsync fails after the move and rollback runs.
        var store = new CountingStore(root, failuresBeforeSuccess: 100);
        var generator = new FFmpegProxyGenerator(store);
        string source = Path.Combine(root, "src.mov");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(source);
        string tempPath = Path.Combine(root, "tmp.mov");
        string finalPath = Path.Combine(root, "hash", "quarter.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        byte[] previousProxy = [7, 7, 7];
        File.WriteAllBytes(finalPath, previousProxy);
        File.WriteAllBytes(tempPath, [9, 9, 9, 9, 9]);
        var job = new ProxyJob(fingerprint, ProxyPreset.Quarter);

        Exception? thrown = Assert.CatchAsync(async () =>
            await generator.PublishAsync(
                tempPath,
                finalPath,
                job,
                "hash/quarter.mp4",
                new PixelSize(64, 48),
                new PixelSize(32, 24),
                CancellationToken.None,
                moveAttempt: (s, d) =>
                {
                    File.Move(s, d, overwrite: true);
                    return true;
                },
                // A metadata backup path that does not exist makes RestoreMetadata's File.Copy fail
                // during rollback; that fault must not mask the primary Register failure.
                metadataBackupAttempt: _ => Path.Combine(root, "missing.bak")));

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.InstanceOf<InvalidOperationException>(), "the primary Register failure must survive a rollback-restore fault");
            Assert.That(File.Exists(finalPath), Is.True, "the previous proxy must be restored from backup");
            Assert.That(File.ReadAllBytes(finalPath), Is.EqualTo(previousProxy));
        });
    }

    [Test]
    public void EncodeAndPublishGuarded_GenericFailure_DeletesTempAndRethrows()
    {
        string temp = CreateTempArtifact();

        InvalidOperationException? thrown = Assert.ThrowsAsync<InvalidOperationException>(
            () => FFmpegProxyGenerator.EncodeAndPublishGuardedAsync(
                temp,
                () => throw new InvalidOperationException("encode failed")));

        Assert.Multiple(() =>
        {
            Assert.That(thrown!.Message, Is.EqualTo("encode failed"));
            Assert.That(File.Exists(temp), Is.False, "a failed generation must not leave its temp artifact behind");
        });
    }

    [Test]
    public void EncodeAndPublishGuarded_Cancellation_DeletesTempAndRethrows()
    {
        string temp = CreateTempArtifact();

        Assert.CatchAsync<OperationCanceledException>(
            () => FFmpegProxyGenerator.EncodeAndPublishGuardedAsync(
                temp,
                () => throw new OperationCanceledException()));

        Assert.That(File.Exists(temp), Is.False, "a canceled generation must not leave its temp artifact behind");
    }

    [Test]
    public void EncodeAndPublishGuarded_LibrariesMissing_DeletesTempAndMapsToUnavailable()
    {
        string temp = CreateTempArtifact();

        Assert.ThrowsAsync<ProxyGeneratorUnavailableException>(
            () => FFmpegProxyGenerator.EncodeAndPublishGuardedAsync(
                temp,
                () => throw new FFmpegLibrariesNotFoundException("libs missing")));

        Assert.That(File.Exists(temp), Is.False);
    }

    [Test]
    public async Task EncodeAndPublishGuarded_Success_LeavesTempAlone()
    {
        string temp = CreateTempArtifact();

        await FFmpegProxyGenerator.EncodeAndPublishGuardedAsync(temp, () => Task.CompletedTask);

        Assert.That(File.Exists(temp), Is.True);
    }

    private static string CreateTempArtifact()
    {
        string root = CreateRoot();
        string temp = Path.Combine(root, $"quarter.{Guid.NewGuid():N}.tmp.mp4");
        File.WriteAllBytes(temp, [1, 2, 3]);
        return temp;
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
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
