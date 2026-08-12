using Beutl.Api.Services;
using Beutl.Services.AI;

namespace Beutl.UnitTests.Services.AI;

[TestFixture]
public sealed class CaptionDraftStoreTests
{
    private string _directory = null!;
    private string _storageDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "Beutl.UnitTests",
            nameof(CaptionDraftStoreTests),
            Guid.NewGuid().ToString("N"));
        _storageDirectory = Path.Combine(_directory, "drafts");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public void SaveLoadDelete_RoundTripsRecoverableProgress()
    {
        var store = new FileCaptionDraftStore(_storageDirectory);
        CaptionDraftScope scope = CreateScope();
        CaptionDraft original = CreateDraft();
        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? session), Is.True);
        using (session)
        {
            session!.Save(new CaptionDraftEntry("job-42", original));
            CaptionDraftEntry? restored = session.Load();

            Assert.That(restored, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(restored!.JobId, Is.EqualTo("job-42"));
                Assert.That(restored.Draft.Version, Is.EqualTo(FileCaptionDraftStore.CurrentVersion));
                Assert.That(restored.Draft.Cues.Single().Text, Is.EqualTo("paid result"));
                Assert.That(restored.Draft.SceneTranscriptionResume?.CompletedChunkCount, Is.EqualTo(1));
                Assert.That(restored.Draft.Segments?.Single().Text, Is.EqualTo("paid result"));
            });

            session.Delete();
            Assert.That(session.Load(), Is.Null);
        }
    }

    [Test]
    public void Load_DeletesMalformedOrOversizedDrafts()
    {
        Directory.CreateDirectory(_storageDirectory);
        var store = new FileCaptionDraftStore(_storageDirectory);
        CaptionDraftScope scope = CreateScope();
        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? session), Is.True);
        using (session)
        {
            string storagePath = store.GetStoragePath(scope);
            File.WriteAllText(storagePath, "{not-json");

            Assert.Multiple(() =>
            {
                Assert.That(session!.Load(), Is.Null);
                Assert.That(File.Exists(storagePath), Is.False);
            });

            File.WriteAllBytes(
                storagePath,
                new byte[FileCaptionDraftStore.MaximumStorageBytes + 1]);
            Assert.Multiple(() =>
            {
                Assert.That(session!.Load(), Is.Null);
                Assert.That(File.Exists(storagePath), Is.False);
            });
        }
    }

    [Test]
    public void Scopes_IsolateUsersProjectsAndScenes()
    {
        var store = new FileCaptionDraftStore(_storageDirectory);
        CaptionDraftScope firstScope = CreateScope();
        CaptionDraftScope[] otherScopes =
        [
            new("other-user", firstScope.ProjectId, firstScope.SceneId),
            new(firstScope.UserId, Guid.NewGuid(), firstScope.SceneId),
            new(firstScope.UserId, firstScope.ProjectId, Guid.NewGuid()),
        ];
        Assert.That(store.TryOpen(firstScope, out ICaptionDraftSession? first), Is.True);
        using (first)
        {
            first!.Save(new CaptionDraftEntry("job-1", CreateDraft()));
            foreach (CaptionDraftScope scope in otherScopes)
            {
                Assert.That(store.TryOpen(scope, out ICaptionDraftSession? other), Is.True);
                using (other)
                {
                    Assert.That(other!.Load(), Is.Null, scope.ToString());
                }
            }
        }
    }

    [Test]
    public void SameScope_HasOneOwnerAndAnotherTabCannotOverwriteOrDeleteItsDraft()
    {
        var store = new FileCaptionDraftStore(_storageDirectory);
        CaptionDraftScope scope = CreateScope();
        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? owner), Is.True);
        owner!.Save(new CaptionDraftEntry("job-1", CreateDraft()));

        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? competing), Is.False);
        Assert.That(competing, Is.Null);
        Assert.That(owner.Load(), Is.Not.Null);

        owner.Dispose();
        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? successor), Is.True);
        using (successor)
        {
            Assert.That(successor!.Load()?.Draft.Cues.Single().Text, Is.EqualTo("paid result"));
        }
    }

    [Test]
    public void JobOwnedDraft_ReopensThroughBaseScopeAfterStoreRecreation()
    {
        CaptionDraftScope scope = CreateScope();
        var firstStore = new FileCaptionDraftStore(_storageDirectory);
        Assert.That(firstStore.TryOpen(scope, out ICaptionDraftSession? firstSession), Is.True);
        using (firstSession)
        {
            firstSession!.Save(new CaptionDraftEntry("paid-job-123", CreateDraft()));
        }

        var recreatedStore = new FileCaptionDraftStore(_storageDirectory);
        Assert.That(recreatedStore.TryOpen(scope, out ICaptionDraftSession? restoredSession), Is.True);
        using (restoredSession)
        {
            CaptionDraftEntry? restored = restoredSession!.Load();
            Assert.Multiple(() =>
            {
                Assert.That(restored?.JobId, Is.EqualTo("paid-job-123"));
                Assert.That(restored?.Draft.Cues.Single().Text, Is.EqualTo("paid result"));
                Assert.That(Directory.GetFiles(_storageDirectory, "*.json"), Has.Length.EqualTo(1));
            });
        }
    }

    private static CaptionDraftScope CreateScope()
        => new("user-1", Guid.NewGuid(), Guid.NewGuid());

    private static CaptionDraft CreateDraft()
    {
        var cue = new StoredCaptionCue(
            TimeSpan.Zero.Ticks,
            TimeSpan.FromSeconds(1).Ticks,
            "paid result",
            null,
            "en",
            new Dictionary<string, string>(StringComparer.Ordinal));
        var segment = new AiTranscriptionSegment
        {
            Start = 0,
            End = 1,
            Text = "paid result",
        };
        return new CaptionDraft(
            FileCaptionDraftStore.CurrentVersion,
            [cue],
            "en",
            [segment],
            CaptionDraftKind.Transcription,
            1,
            2,
            null,
            new CaptionSceneTranscriptionResume(
                Guid.NewGuid(),
                "00:00:00.000",
                "00:00:02.000",
                null,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(1),
                2,
                [segment],
                "en",
                1));
    }
}
