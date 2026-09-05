using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Api.Services;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class AiRetryKeyStoreTests
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
    public void RestartAndTwoInstancesReuseExactIdentityWithoutClobberingOthers()
    {
        var first = new FileAiRetryKeyStore(_directory);
        AiJob jobA = Job("job-a", "prompt-a");
        AiJob jobB = Job("job-b", "prompt-b");
        string keyA = first.GetOrCreate(jobA, "account", out bool repeatA);
        var second = new FileAiRetryKeyStore(_directory);
        string keyB = second.GetOrCreate(jobB, "account", out bool repeatB);
        var restarted = new FileAiRetryKeyStore(_directory);

        Assert.Multiple(() =>
        {
            Assert.That(repeatA, Is.False);
            Assert.That(repeatB, Is.False);
            Assert.That(restarted.GetOrCreate(jobA, "account", out bool againA), Is.EqualTo(keyA));
            Assert.That(againA, Is.True);
            Assert.That(restarted.GetOrCreate(jobB, "account", out bool againB), Is.EqualTo(keyB));
            Assert.That(againB, Is.True);
        });
    }

    [Test]
    public void AccountAndCanonicalPayloadAreSeparateIdentities()
    {
        var store = new FileAiRetryKeyStore(_directory);
        AiJob firstBody = Job("job", "first");
        AiJob changedBody = Job("job", "second");

        string first = store.GetOrCreate(firstBody, "account-a", out _);
        string otherAccount = store.GetOrCreate(firstBody, "account-b", out bool accountRepeat);
        Assert.Throws<AiRetryAttemptRejectedException>(() =>
            store.GetOrCreate(changedBody, "account-a", out _));

        Assert.Multiple(() =>
        {
            Assert.That(accountRepeat, Is.False);
            Assert.That(otherAccount, Is.Not.EqualTo(first));
        });
    }

    [Test]
    public void RetireRemovesOnlyTheMatchingIdentity()
    {
        var store = new FileAiRetryKeyStore(_directory);
        AiJob firstJob = Job("first", "one");
        AiJob secondJob = Job("second", "two");
        string first = store.GetOrCreate(firstJob, "account", out _);
        string second = store.GetOrCreate(secondJob, "account", out _);

        store.Retire(firstJob, "account");
        var restarted = new FileAiRetryKeyStore(_directory);
        string nextFirst = restarted.GetOrCreate(firstJob, "account", out bool firstRepeat);
        string sameSecond = restarted.GetOrCreate(secondJob, "account", out bool secondRepeat);

        Assert.Multiple(() =>
        {
            Assert.That(firstRepeat, Is.False);
            Assert.That(nextFirst, Is.Not.EqualTo(first));
            Assert.That(secondRepeat, Is.True);
            Assert.That(sameSecond, Is.EqualTo(second));
        });
    }

    [Test]
    public void RecoveryAttemptRetireRaceFailsWithoutCreatingAnotherKey()
    {
        var first = new FileAiRetryKeyStore(_directory);
        AiJob job = Job("recovery-race", "prompt");
        string original = first.GetOrCreate(job, "account", out _);
        AiRetryAttempt attempt = first.PrepareAttempt(job, "account");

        new FileAiRetryKeyStore(_directory).Retire(job, "account");

        Assert.Multiple(() =>
        {
            Assert.That(
                first.TryConsumeAttempt(attempt, job, "account", out _, out _),
                Is.False);
            Assert.That(first.TryGet(job, "account", out _), Is.False);
        });

        string fresh = first.GetOrCreate(job, "account", out bool isRepeat);
        Assert.That(isRepeat, Is.False);
        Assert.That(fresh, Is.Not.EqualTo(original));
    }

    [Test]
    public void AttemptIsBoundToTheAuthenticatedAccount()
    {
        var store = new FileAiRetryKeyStore(_directory);
        AiJob job = Job("account-binding", "prompt");
        string original = store.GetOrCreate(job, "account-a", out _);
        AiRetryAttempt attempt = store.PrepareAttempt(job, "account-a");

        Assert.That(
            store.TryConsumeAttempt(attempt, job, "account-b", out _, out _),
            Is.False);
        Assert.That(store.TryGet(job, "account-a", out string retained), Is.True);
        Assert.That(retained, Is.EqualTo(original));
    }

    [Test]
    public async Task TwoStoreInstancesConsumeOnePendingAttempt()
    {
        var first = new FileAiRetryKeyStore(_directory);
        var second = new FileAiRetryKeyStore(_directory);
        AiJob job = Job("concurrent-confirm", "prompt");
        AiRetryAttempt firstAttempt = first.PrepareAttempt(job, "account");
        AiRetryAttempt secondAttempt = second.PrepareAttempt(job, "account");

        (bool First, string FirstKey, bool Second, string SecondKey) result = await Task.WhenAll(
                Task.Run(() => Consume(first, firstAttempt)),
                Task.Run(() => Consume(second, secondAttempt)))
            .ContinueWith(static task =>
            {
                (bool Success, string Key)[] values = task.Result;
                return (values[0].Success, values[0].Key, values[1].Success, values[1].Key);
            });

        Assert.Multiple(() =>
        {
            Assert.That(new[] { result.First, result.Second }.Count(value => value), Is.EqualTo(1));
            Assert.That(
                result.First ? result.FirstKey : result.SecondKey,
                Does.StartWith("history-retry:concurrent-confirm:"));
        });

        static (bool Success, string Key) Consume(
            FileAiRetryKeyStore store,
            AiRetryAttempt attempt)
            => store.TryConsumeAttempt(
                attempt,
                Job("concurrent-confirm", "prompt"),
                "account",
                out string key,
                out _)
                ? (true, key)
                : (false, string.Empty);
    }

    [Test]
    public void ExpiredClaimCanRecoverTheExactKeyAfterRestart()
    {
        var store = new FileAiRetryKeyStore(_directory);
        AiJob job = Job("expired-claim", "prompt");
        AiRetryAttempt purchase = store.PrepareAttempt(job, "account");
        Assert.That(
            store.TryConsumeAttempt(purchase, job, "account", out string key, out bool repeat),
            Is.True);
        Assert.That(repeat, Is.False);

        string path = Path.Combine(_directory, "retry-keys.json");
        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        foreach (JsonNode? node in root["entries"]!.AsArray())
        {
            node!.AsObject()["inFlightUntil"] = DateTimeOffset.UtcNow.AddMinutes(-1);
        }
        File.WriteAllText(path, root.ToJsonString());

        var restarted = new FileAiRetryKeyStore(_directory);
        AiRetryAttempt recovery = restarted.PrepareAttempt(job, "account");
        Assert.Multiple(() =>
        {
            Assert.That(recovery.Kind, Is.EqualTo(AiRetryAttemptKind.Recovery));
            Assert.That(recovery.Key, Is.EqualTo(key));
            Assert.That(
                restarted.TryConsumeAttempt(recovery, job, "account", out string retryKey, out bool retryRepeat),
                Is.True);
            Assert.That(retryKey, Is.EqualTo(key));
            Assert.That(retryRepeat, Is.True);
        });
    }

    [Test]
    public void AmbiguousResponseReleasesClaimWithoutChangingTheKey()
    {
        var store = new FileAiRetryKeyStore(_directory);
        AiJob job = Job("response-loss", "prompt");
        AiRetryAttempt purchase = store.PrepareAttempt(job, "account");
        Assert.That(store.TryConsumeAttempt(purchase, job, "account", out string key, out _), Is.True);
        Assert.That(
            store.TryRelease(job, "account", key, purchase.Generation + 1, purchase.Token),
            Is.True);

        AiRetryAttempt recovery = store.PrepareAttempt(job, "account");
        Assert.That(recovery.Key, Is.EqualTo(key));
        Assert.That(recovery.Kind, Is.EqualTo(AiRetryAttemptKind.Recovery));
    }

    [Test]
    public void NewPurchaseAttemptCreatesOnlyWhenConfirmed()
    {
        var store = new FileAiRetryKeyStore(_directory);
        AiJob job = Job("new-purchase", "prompt");
        AiRetryAttempt attempt = store.PrepareAttempt(job, "account");
        Assert.That(store.TryGet(job, "account", out _), Is.False);
        Assert.That(store.TryConsumeAttempt(attempt, job, "account", out string key, out bool repeat), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(repeat, Is.False);
            Assert.That(store.TryGet(job, "account", out string persisted), Is.True);
            Assert.That(persisted, Is.EqualTo(key));
        });
    }

    [Test]
    public void SemanticPayloadFormattingKeepsKey()
    {
        var store = new FileAiRetryKeyStore(_directory);
        AiJob first = JobWithInput("semantic", "{\"prompt\":\"same\",\"seed\":1}");
        AiJob equivalent = JobWithInput("semantic", "{ \"seed\": 1.0e0, \"prompt\": \"same\" }");
        string key = store.GetOrCreate(first, "account", out _);

        Assert.That(store.GetOrCreate(equivalent, "account", out bool repeat), Is.EqualTo(key));
        Assert.That(repeat, Is.True);
    }

    [Test]
    public void ChangedPayloadRejectsPreparedAttempt()
    {
        var store = new FileAiRetryKeyStore(_directory);
        AiJob original = JobWithInput("changed", "{\"prompt\":\"before\"}");
        AiJob changed = JobWithInput("changed", "{\"prompt\":\"after\"}");
        store.GetOrCreate(original, "account", out _);
        AiRetryAttempt attempt = store.PrepareAttempt(original, "account");

        Assert.That(
            store.TryConsumeAttempt(attempt, changed, "account", out _, out _),
            Is.False);
        Assert.That(store.TryGet(original, "account", out _), Is.True);
    }

    [Test]
    public void CanceledConfirmationsDoNotFillStore()
    {
        var store = new FileAiRetryKeyStore(_directory);
        for (int index = 0; index < 300; index++)
        {
            AiJob job = JobWithInput($"cancel-{index}", "{\"prompt\":\"same\"}");
            AiRetryAttempt attempt = store.PrepareAttempt(job, "account");
            attempt.Dispose();
        }

        Assert.That(
            store.PrepareAttempt(JobWithInput("after-cancel", "{\"prompt\":\"same\"}"), "account"),
            Is.Not.Null);
    }

    [Test]
    public void ExpiredPendingIsPrunedOnRestart()
    {
        var store = new FileAiRetryKeyStore(_directory);
        AiRetryAttempt attempt = store.PrepareAttempt(JobWithInput("expired", "{\"prompt\":\"same\"}"), "account");
        string path = Path.Combine(_directory, "retry-keys.json");
        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["attempts"]!.AsArray()[0]!["createdAt"] = DateTimeOffset.UtcNow.AddMinutes(-2).ToString("O");
        root["attempts"]!.AsArray()[0]!["expiresAt"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
        File.WriteAllText(path, root.ToJsonString());

        var restarted = new FileAiRetryKeyStore(_directory);
        Assert.That(
            restarted.PrepareAttempt(JobWithInput("expired", "{\"prompt\":\"same\"}"), "account").Token,
            Is.Not.EqualTo(attempt.Token));
    }

    [Test]
    public void MoreThanGenerationLimitOfSettledDistinctJobsSucceeds()
    {
        var store = new FileAiRetryKeyStore(_directory);
        for (int index = 0; index < 600; index++)
        {
            AiJob job = JobWithInput($"settled-{index}", "{\"prompt\":\"same\"}");
            string key = store.GetOrCreate(job, "account", out _);
            AiRetryAttempt attempt = store.PrepareAttempt(job, "account");
            Assert.That(store.TryConsumeAttempt(attempt, job, "account", out _, out _), Is.True);
            Assert.That(store.TryRetire(job, "account", key, attempt.Generation, attempt.Token), Is.True);
        }

        AiJob active = JobWithInput("active", "{\"prompt\":\"same\"}");
        string activeKey = store.GetOrCreate(active, "account", out _);
        Assert.That(store.TryGet(active, "account", out string retained), Is.True);
        Assert.That(retained, Is.EqualTo(activeKey));
    }

    [Test]
    public void DisposingAttemptUnderLockIsNonThrowing()
    {
        var store = new FileAiRetryKeyStore(_directory);
        AiRetryAttempt attempt = store.PrepareAttempt(JobWithInput("locked", "{\"prompt\":\"same\"}"), "account");
        using (new FileStream(
            Path.Combine(_directory, "retry-keys.json.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None))
        {
            Assert.DoesNotThrow(attempt.Dispose);
        }
    }

    [TestCase("{")]
    [TestCase("{\"version\":2,\"entries\":[]}")]
    [TestCase("{\"version\":1,\"entries\":[],\"unexpected\":true}")]
    public void CorruptFutureAndUnknownShapeFailClosed(string contents)
    {
        File.WriteAllText(Path.Combine(_directory, "retry-keys.json"), contents);
        var store = new FileAiRetryKeyStore(_directory);

        Assert.Throws<AiRetryStoreUnavailableException>(() =>
            store.GetOrCreate(Job("job", "prompt"), "account", out _));
    }

    [Test]
    public void OversizeAndDuplicateDataFailClosed()
    {
        string path = Path.Combine(_directory, "retry-keys.json");
        File.WriteAllBytes(path, new byte[1024 * 1024 + 1]);
        var oversized = new FileAiRetryKeyStore(_directory);
        Assert.Throws<AiRetryStoreUnavailableException>(() =>
            oversized.GetOrCreate(Job("job", "prompt"), "account", out _));

        string identity = new('A', 64);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            version = 1,
            entries = new[]
            {
                new { identity, key = "key-one" },
                new { identity, key = "key-two" },
            },
        }));
        var duplicate = new FileAiRetryKeyStore(_directory);
        Assert.Throws<AiRetryStoreUnavailableException>(() =>
            duplicate.GetOrCreate(Job("job", "prompt"), "account", out _));
    }

    [Test]
    public void LockContentionFailsClosedBeforeIssuingAKey()
    {
        var store = new FileAiRetryKeyStore(_directory);
        using var lease = new FileStream(
            Path.Combine(_directory, "retry-keys.json.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        Assert.Throws<AiRetryStoreUnavailableException>(() =>
            store.GetOrCreate(Job("job", "prompt"), "account", out _));
    }

    [Test]
    public void FullStoreRejectsNewIdentityWithoutEvictingUnresolvedKeys()
    {
        var store = new FileAiRetryKeyStore(_directory);
        AiJob firstJob = Job("job-000", "prompt-000");
        string first = store.GetOrCreate(firstJob, "account", out _);
        for (int index = 1; index < 256; index++)
            store.GetOrCreate(Job($"job-{index:000}", $"prompt-{index:000}"), "account", out _);

        Assert.Throws<AiRetryStoreUnavailableException>(() =>
            store.GetOrCreate(Job("overflow", "overflow"), "account", out _));

        var restarted = new FileAiRetryKeyStore(_directory);
        Assert.That(restarted.GetOrCreate(firstJob, "account", out bool repeat), Is.EqualTo(first));
        Assert.That(repeat, Is.True);
    }

    [Test]
    public void StoreFilesArePrivateOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix modes are not available on Windows.");
            return;
        }
        var store = new FileAiRetryKeyStore(_directory);
        store.GetOrCreate(Job("job", "prompt"), "account", out _);

#pragma warning disable CA1416 // Guarded above; these APIs are unavailable only on Windows.
        Assert.Multiple(() =>
        {
            Assert.That(
                File.GetUnixFileMode(_directory),
                Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
            Assert.That(
                File.GetUnixFileMode(Path.Combine(_directory, "retry-keys.json")),
                Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        });
#pragma warning restore CA1416
    }

    [Test]
    public void RestartSweepsOnlyOldRetryStoreTemporaryFiles()
    {
        string stale = Path.Combine(_directory, "retry-keys.json.abc.tmp");
        string fresh = Path.Combine(_directory, "retry-keys.json.def.tmp");
        File.WriteAllText(stale, "stale");
        File.WriteAllText(fresh, "fresh");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));

        _ = new FileAiRetryKeyStore(_directory);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(stale), Is.False);
            Assert.That(File.Exists(fresh), Is.True);
        });
    }

    [Test]
    public void RestartDoesNotDeleteAnOldRetryTempStillHeldOpen()
    {
        string path = Path.Combine(_directory, "retry-keys.json.held.tmp");
        File.WriteAllText(path, "in-progress");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));
        using FileStream held = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        _ = new FileAiRetryKeyStore(_directory);

        Assert.That(File.Exists(path), Is.True);
    }

    private static AiJob Job(string id, string prompt) => new(
        new AiJobId(id),
        AiJobKinds.Image,
        AiJobStatuses.Failed,
        JsonDocument.Parse($$"""{"prompt":"{{prompt}}","aspectRatio":"1:1"}""").RootElement.Clone(),
        FileId: null,
        ContentUri: null,
        Error: "aiProviderError",
        CanRetry: true,
        CreatedAt: DateTimeOffset.UnixEpoch,
        UpdatedAt: DateTimeOffset.UnixEpoch);

    private static AiJob JobWithInput(string id, string input) => new(
        new AiJobId(id),
        AiJobKinds.Image,
        AiJobStatuses.Failed,
        JsonDocument.Parse(input).RootElement.Clone(),
        null,
        null,
        "aiProviderError",
        true,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);
}
