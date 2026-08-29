using Beutl.Services.AI;

namespace Beutl.UnitTests.Services.AI;

[TestFixture]
public sealed class FileAiRequestRecoveryStoreTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "Beutl.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public void RestartAndTwoInstancesMergeWithoutClobbering()
    {
        var first = new FileAiRequestRecoveryStore(_directory);
        var a = Record("account", "image.generate", "fingerprint-a", "key-a");
        var b = Record("account", "video.generate", "fingerprint-b", "key-b");
        Assert.That(first.WriteOrGet(a).Key, Is.EqualTo("key-a"));
        var second = new FileAiRequestRecoveryStore(_directory);
        Assert.That(second.WriteOrGet(b).Key, Is.EqualTo("key-b"));

        var restarted = new FileAiRequestRecoveryStore(_directory);
        Assert.Multiple(() =>
        {
            Assert.That(restarted.Find(a.AccountId, a.Operation, a.Fingerprint)?.Key, Is.EqualTo("key-a"));
            Assert.That(restarted.Find(b.AccountId, b.Operation, b.Fingerprint)?.Key, Is.EqualTo("key-b"));
        });
    }

    [Test]
    public void ConcurrentIdentityKeepsTheFirstDurableKey()
    {
        var first = new FileAiRequestRecoveryStore(_directory);
        var second = new FileAiRequestRecoveryStore(_directory);
        var original = Record("account", "image.generate", "same", "first-key");
        var competing = original with { Key = "second-key" };

        Assert.That(first.WriteOrGet(original).Key, Is.EqualTo("first-key"));
        Assert.That(second.WriteOrGet(competing).Key, Is.EqualTo("first-key"));
        Assert.That(second.Find(original.AccountId, original.Operation, original.Fingerprint)?.Key, Is.EqualTo("first-key"));
    }

    [Test]
    public void AccountOperationAndFingerprintAreIndependent()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        AiPendingAttempt[] records =
        [
            Record("a", "image.generate", "same", "key-a"),
            Record("b", "image.generate", "same", "key-b"),
            Record("a", "video.generate", "same", "key-video"),
            Record("a", "image.generate", "other", "key-other"),
        ];
        foreach (AiPendingAttempt record in records)
            store.WriteOrGet(record);

        Assert.That(records.Select(record =>
            store.Find(record.AccountId, record.Operation, record.Fingerprint)?.Key),
            Is.EqualTo(records.Select(record => record.Key)));
    }

    [Test]
    public void RemoveDeletesOnlyTheExactIdentity()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        var first = Record("account", "image.generate", "first", "key-first");
        var second = Record("account", "image.generate", "second", "key-second");
        store.WriteOrGet(first);
        store.WriteOrGet(second);

        Assert.That(
            store.TrySettle(first.AccountId, first.Operation, first.Fingerprint, first.Key),
            Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(store.Find(first.AccountId, first.Operation, first.Fingerprint), Is.Null);
            Assert.That(store.Find(second.AccountId, second.Operation, second.Fingerprint)?.Key, Is.EqualTo(second.Key));
        });
    }

    [TestCase("{")]
    [TestCase("{\"version\":2,\"records\":[]}")]
    [TestCase("{\"version\":1,\"records\":[],\"unexpected\":true}")]
    public void CorruptFutureAndUnknownShapeFailClosed(string contents)
    {
        File.WriteAllText(Path.Combine(_directory, "ai-request-recovery.json"), contents);
        var store = new FileAiRequestRecoveryStore(_directory);

        Assert.Throws<InvalidDataException>(() => store.Find("account", "image.generate", "fingerprint"));
    }

    [Test]
    public void OversizeInvalidAndLockContentionFailClosed()
    {
        string path = Path.Combine(_directory, "ai-request-recovery.json");
        File.WriteAllBytes(path, new byte[1024 * 1024 + 1]);
        var oversized = new FileAiRequestRecoveryStore(_directory);
        Assert.Throws<InvalidDataException>(() => oversized.Find("account", "image.generate", "fingerprint"));

        File.Delete(path);
        var store = new FileAiRequestRecoveryStore(_directory);
        Assert.Throws<InvalidDataException>(() => store.WriteOrGet(
            Record("account", "image.generate", "fingerprint", "bad\nkey")));
        using var lease = new FileStream(
            path + ".lock",
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        Assert.Throws<InvalidDataException>(() => store.Find("account", "image.generate", "fingerprint"));
    }

    [Test]
    public void FullStoreRejectsNewRecordWithoutEvictingUnresolvedRecords()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        var first = Record("account", "image.generate", "fingerprint-000", "key-000");
        store.WriteOrGet(first);
        for (int index = 1; index < 256; index++)
        {
            store.WriteOrGet(Record(
                "account",
                "image.generate",
                $"fingerprint-{index:000}",
                $"key-{index:000}"));
        }

        Assert.Throws<InvalidDataException>(() => store.WriteOrGet(
            Record("account", "image.generate", "overflow", "overflow-key")));
        var restarted = new FileAiRequestRecoveryStore(_directory);
        Assert.That(restarted.Find(first.AccountId, first.Operation, first.Fingerprint)?.Key, Is.EqualTo(first.Key));
    }

    [Test]
    public void StoreFilesArePrivateOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix modes are not available on Windows.");
            return;
        }
        var store = new FileAiRequestRecoveryStore(_directory);
        store.WriteOrGet(Record("account", "image.generate", "fingerprint", "key"));

#pragma warning disable CA1416 // Guarded above; these APIs are unavailable only on Windows.
        Assert.Multiple(() =>
        {
            Assert.That(
                File.GetUnixFileMode(_directory),
                Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
            Assert.That(
                File.GetUnixFileMode(Path.Combine(_directory, "ai-request-recovery.json")),
                Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        });
#pragma warning restore CA1416
    }

    [Test]
    public void FormSnapshotAndExternalSourceRoundTripAndFailClosedOnChange()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        string sourcePath = Path.Combine(_directory, "source.png");
        byte[] bytes = [1, 2, 3, 4];
        File.WriteAllBytes(sourcePath, bytes);
        var source = FileAiRequestRecoveryStore.CreateExternalSource(
            "image", sourcePath, "source.png", bytes);
        var attempt = new AiPendingAttempt(
            "account",
            "image.edit.upscale",
            "fp",
            "key",
            "model",
            new AiRequestFormSnapshot(
                Prompt: "line 1\nline 2\twith tab",
                Task: "upscale",
                AspectRatio: "1:1",
                Seed: 42),
            [source]);
        store.WriteOrGet(attempt);
        AiPendingAttempt? restarted = new FileAiRequestRecoveryStore(_directory)
            .Find("account", "image.edit.upscale", "fp");
        Assert.That(restarted, Is.Not.Null);
        Assert.That(restarted!.Form!.Prompt, Is.EqualTo("line 1\nline 2\twith tab"));
        Assert.That(store.TryResolveSource(restarted.EffectiveSources[0], out string? resolved), Is.True);
        Assert.That(resolved, Is.EqualTo(sourcePath));

        File.WriteAllBytes(sourcePath, [9, 8, 7]);
        Assert.That(store.TryResolveSource(restarted.EffectiveSources[0], out _), Is.False);
    }

    [Test]
    public void DurableSourceIsCleanedOnAbandonButRetainedWhileReferenced()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        AiRequestRecoverySource source = store.CreateDurableSource("frame", "frame.png", [1, 2, 3]);
        string sourcePath = Path.Combine(store.SourceDirectory, source.DurableFile!);
        Assert.That(File.Exists(sourcePath), Is.True);
        var first = new AiPendingAttempt("account", "video.generate", "first", "key-first", null, new AiRequestFormSnapshot(), [source]);
        var second = first with { Fingerprint = "second", Key = "key-second" };
        store.WriteOrGet(first);
        store.WriteOrGet(second);
        store.Abandon(first);
        Assert.That(File.Exists(sourcePath), Is.True);
        store.Abandon(second);
        Assert.That(File.Exists(sourcePath), Is.False);
    }

    [Test]
    public void ExplicitNullModelIsRetainedWithMultipleRows()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        store.WriteOrGet(new AiPendingAttempt("account", "image.generate", "a", "key-a", null, new AiRequestFormSnapshot()));
        store.WriteOrGet(new AiPendingAttempt("account", "image.generate", "b", "key-b", "model-b", new AiRequestFormSnapshot()));
        Assert.That(store.HasModelless("account", "image.generate"), Is.True);
        Assert.That(store.ModelsFor("account", "image.generate"), Is.EqualTo(new[] { "model-b" }));
    }

    [Test]
    public void CanonicalSnapshotsForAllFormsSurviveRestart()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        AiRequestFormSnapshot[] forms =
        [
            new(Prompt: "image\nwith tab\t", AspectRatio: "3:2", Background: "transparent", Seed: 12),
            new(Prompt: "edit", Task: "upscale", SourceName: "source.png", SourceElementId: "element-a"),
            new(Prompt: "video", DurationSeconds: 8, Resolution: "1080p", AspectRatio: "16:9", GenerateAudio: false, Seed: 3),
        ];
        string[] operations = ["image.generate", "image.edit.upscale", "video.generate"];
        for (int index = 0; index < forms.Length; index++)
        {
            store.WriteOrGet(new AiPendingAttempt(
                "account",
                operations[index],
                $"fingerprint-{index}",
                $"key-{index}",
                index == 1 ? "edit-model" : null,
                forms[index]));
        }

        var restarted = new FileAiRequestRecoveryStore(_directory);
        AiPendingAttempt[] attempts = [
            restarted.PendingFor("account", operations[0]).Single(),
            restarted.PendingFor("account", operations[1]).Single(),
            restarted.PendingFor("account", operations[2]).Single(),
        ];
        Assert.Multiple(() =>
        {
            Assert.That(attempts[0].Form, Is.EqualTo(forms[0]));
            Assert.That(attempts[1].Form, Is.EqualTo(forms[1]));
            Assert.That(attempts[2].Form, Is.EqualTo(forms[2]));
            Assert.That(File.ReadAllText(restarted.StoragePath), Does.Not.Contain("010203"));
        });
    }

    [Test]
    public void RestartOrphanSweepDeletesOnlyOldUnreferencedCopies()
    {
        var first = new FileAiRequestRecoveryStore(_directory);
        AiRequestRecoverySource orphan = first.CreateDurableSource("frame", "frame.png", [4, 5, 6]);
        string orphanPath = Path.Combine(first.SourceDirectory, orphan.DurableFile!);
        File.SetLastWriteTimeUtc(orphanPath, DateTime.UtcNow.AddHours(-2));
        var retainedSource = first.CreateDurableSource("frame", "retained.png", [7, 8, 9]);
        string retainedPath = Path.Combine(first.SourceDirectory, retainedSource.DurableFile!);
        first.WriteOrGet(new AiPendingAttempt(
            "account",
            "video.generate",
            "retained",
            "retained-key",
            null,
            new AiRequestFormSnapshot(DurationSeconds: 4),
            [retainedSource]));
        File.SetLastWriteTimeUtc(retainedPath, DateTime.UtcNow.AddHours(-2));

        _ = new FileAiRequestRecoveryStore(_directory);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(orphanPath), Is.False);
            Assert.That(File.Exists(retainedPath), Is.True);
        });
    }

    [Test]
    public void StaleExactRemovalCannotDeleteNewKey()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        var current = new AiPendingAttempt(
            "account",
            "video.generate",
            "same",
            "key-new",
            "model",
            new AiRequestFormSnapshot(DurationSeconds: 4));
        store.WriteOrGet(current);

        Assert.Multiple(() =>
        {
            Assert.That(store.TrySettle("account", "video.generate", "same", "key-old"), Is.False);
            Assert.That(store.TryWithdraw("account", "video.generate", "same", "key-old"), Is.False);
            Assert.That(store.Find("account", "video.generate", "same")?.Key, Is.EqualTo("key-new"));
        });
    }

    [Test]
    public void ClaimCompetesAcrossStoreInstancesAndSettleInvalidatesIt()
    {
        var firstStore = new FileAiRequestRecoveryStore(_directory);
        var secondStore = new FileAiRequestRecoveryStore(_directory);
        var attempt = new AiPendingAttempt(
            "account",
            "image.generate",
            "claim-fingerprint",
            "claim-key",
            null,
            new AiRequestFormSnapshot(Prompt: "claim"));
        firstStore.WriteOrGet(attempt);
        using AiRequestRecoveryLease first = firstStore.Claim(
            attempt.AccountId,
            attempt.Operation,
            attempt.Fingerprint,
            attempt.Key);
        first.MarkDispatched();
        using AiRequestRecoveryLease second = secondStore.Claim(
            attempt.AccountId,
            attempt.Operation,
            attempt.Fingerprint,
            attempt.Key);
        Assert.That(secondStore.Abandon(attempt), Is.False);
        Assert.That(firstStore.TrySettle(
            attempt.AccountId,
            attempt.Operation,
            attempt.Fingerprint,
            attempt.Key), Is.False);
        first.Dispose();
        Assert.That(secondStore.Abandon(attempt), Is.False,
            "The competing claim remains the dispatch fence until it expires.");
    }

    [Test]
    public void PredispatchClaimCanBeReleasedAndAbandoned()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        var attempt = new AiPendingAttempt(
            "account",
            "video.generate",
            "predispatch",
            "predispatch-key",
            null,
            new AiRequestFormSnapshot(Prompt: "video"));
        store.WriteOrGet(attempt);
        using (store.Claim(attempt.AccountId, attempt.Operation, attempt.Fingerprint, attempt.Key)) { }
        Assert.That(store.Abandon(attempt), Is.True);
    }

    [Test]
    public void GenerationTombstonesPruneSettledIdentities()
    {
        var store = new FileAiRequestRecoveryStore(_directory);
        for (int index = 0; index < 1_100; index++)
        {
            var attempt = new AiPendingAttempt(
                "account",
                "image.generate",
                $"generation-{index}",
                $"generation-key-{index}",
                null,
                new AiRequestFormSnapshot(Prompt: index.ToString()));
            store.WriteOrGet(attempt);
            Assert.That(store.TrySettle(
                attempt.AccountId,
                attempt.Operation,
                attempt.Fingerprint,
                attempt.Key), Is.True);
        }

        var active = new AiPendingAttempt(
            "account",
            "image.generate",
            "active-generation",
            "active-generation-key",
            null,
            new AiRequestFormSnapshot(Prompt: "active"));
        store.WriteOrGet(active);
        Assert.That(store.Find(active.AccountId, active.Operation, active.Fingerprint)?.Key,
            Is.EqualTo(active.Key));
    }

    private static AiPendingAttempt Record(
        string account,
        string operation,
        string fingerprint,
        string key)
        => new(account, operation, fingerprint, key, Model: null);
}
