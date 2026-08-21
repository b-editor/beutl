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

    [Test]
    public void Save_KeepsARunThatHasOnlyNamedItsFirstPiece()
    {
        // 最初のひと切れは、課金されたまま失われる可能性がいちばん高い。それが
        // 返ってくるまで何も書き残さないと、その名前はセッションと一緒に消え、
        // 次の実行は同じ切れ端を別の名前で買い直すことになる。
        var store = new FileCaptionDraftStore(_storageDirectory);
        CaptionDraftScope scope = CreateScope();
        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? session), Is.True);
        using (session)
        {
            session!.Save(new CaptionDraftEntry(null, CreateUnstartedDraft()));
            CaptionDraftEntry? restored = session.Load();

            Assert.That(restored, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(restored!.Draft.CompletedSteps, Is.Zero);
                Assert.That(
                    restored.Draft.SourceTranscriptionResume?.RequestKeySeed,
                    Is.EqualTo("seed-of-the-run"));
                Assert.That(
                    restored.Draft.SourceTranscriptionResume?.RequestKeyModel,
                    Is.EqualTo("openai/whisper-1"));
            });
        }
    }

    [Test]
    public void Load_DiscardsADraftWrittenByAnEarlierVersion()
    {
        // 版 1 の控えは、seed を持ちながらモデルを持たないことがある——それは
        // 「モデルを指定しなかった実行」と見分けがつかない。取り違えると、支払い
        // 済みの切れ端に別の名前を付けて買い直すので、当て推量ではなく捨てる。
        var store = new FileCaptionDraftStore(_storageDirectory);
        CaptionDraftScope scope = CreateScope();
        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? session), Is.True);
        using (session)
        {
            session!.Save(new CaptionDraftEntry("job-1", CreateDraft()));
        }

        string path = store.GetStoragePath(scope);
        string stored = File.ReadAllText(path);
        File.WriteAllText(path, stored.Replace("\"version\":2", "\"version\":1"));

        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? reopened), Is.True);
        using (reopened)
        {
            Assert.That(reopened!.Load(), Is.Null);
        }
    }

    private static CaptionDraft CreateUnstartedDraft()
    {
        return new CaptionDraft(
            FileCaptionDraftStore.CurrentVersion,
            [],
            "en",
            [],
            CaptionDraftKind.Transcription,
            0,
            3,
            null,
            null,
            new CaptionSourceTranscriptionResume(
                Path.Combine(Path.GetTempPath(), "clip.wav"),
                Guid.NewGuid(),
                1024,
                DateTime.UnixEpoch.Ticks,
                "en",
                16_000,
                48_000,
                16_000,
                3,
                [],
                null,
                0,
                "seed-of-the-run",
                "openai/whisper-1"));
    }

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
                null,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(1),
                2,
                [segment],
                "en",
                1));
    }

    [Test]
    public void TranslationResume_RoundTripsTheNameItsBatchesWereSentUnder()
    {
        var store = new FileCaptionDraftStore(_storageDirectory);
        CaptionDraftScope scope = CreateScope();
        var draft = new CaptionDraft(
            FileCaptionDraftStore.CurrentVersion,
            [new StoredCaptionCue(0, TimeSpan.FromSeconds(1).Ticks, "Hello", null, "en", [])],
            "ja",
            null,
            CaptionDraftKind.Translation,
            1,
            2,
            new CaptionTranslationResume(
                [new StoredCaptionCue(0, TimeSpan.FromSeconds(1).Ticks, "Hello", null, "en", [])],
                "en",
                "en",
                "ja",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["line-1"] = "こんにちは" },
                1,
                "8c1d7e2a5b904f36a1e0c4d8f7b62039",
                "openai/gpt-5"),
            null);

        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? session), Is.True);
        using (session)
        {
            session!.Save(new CaptionDraftEntry("job-translation", draft));
        }

        Assert.That(store.TryOpen(scope, out session), Is.True);
        using (session)
        {
            CaptionDraftEntry? restored = session!.Load();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(restored, Is.Not.Null);
                Assert.That(restored!.Draft.TranslationResume, Is.Not.Null);
                Assert.That(restored.Draft.TranslationResume!.CompletedBatchCount, Is.EqualTo(1));
                // The name the unfinished batches will be asked for under. Without
                // it a run resumed in a later session would buy a batch the first
                // session may already have paid for.
                Assert.That(
                    restored.Draft.TranslationResume.RequestKeySeed,
                    Is.EqualTo("8c1d7e2a5b904f36a1e0c4d8f7b62039"));
                // 名前はモデルまで含めて作られる。どのモデルで走っていたかを
                // 覚えていないと、再開時に別のモデルが選ばれ、未完了のバッチが
                // 別の名前で送られて二重に課金される。
                Assert.That(
                    restored.Draft.TranslationResume.RequestKeyModel,
                    Is.EqualTo("openai/gpt-5"));
            }
        }
    }

    [Test]
    public void SourceTranscriptionResume_RoundTripsIncompleteProgress()
    {
        var store = new FileCaptionDraftStore(_storageDirectory);
        CaptionDraftScope scope = CreateScope();
        var segment = new AiTranscriptionSegment
        {
            Start = 0,
            End = 1,
            Text = "decoded source",
        };
        var draft = new CaptionDraft(
            FileCaptionDraftStore.CurrentVersion,
            [new StoredCaptionCue(0, TimeSpan.FromSeconds(1).Ticks, segment.Text, null, "en", [])],
            "en",
            [segment],
            CaptionDraftKind.Transcription,
            1,
            2,
            null,
            null,
            new CaptionSourceTranscriptionResume(
                Path.GetFullPath("source.flac"),
                Guid.Empty,
                1_024,
                DateTime.UtcNow.Ticks,
                "en",
                48_000,
                57_600_000,
                28_800_000,
                2,
                [segment],
                "en",
                1,
                "1f4c2b0d9e6a4f118b7c3d5e6f708192",
                "openai/whisper-1"));

        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? session), Is.True);
        using (session)
        {
            session!.Save(new CaptionDraftEntry("job-source", draft));
        }

        Assert.That(store.TryOpen(scope, out session), Is.True);
        using (session)
        {
            CaptionDraftEntry? restored = session!.Load();
            Assert.Multiple(() =>
            {
                Assert.That(restored, Is.Not.Null);
                Assert.That(restored!.Draft.SourceTranscriptionResume, Is.Not.Null);
                Assert.That(
                    restored.Draft.SourceTranscriptionResume!.CompletedChunkCount,
                    Is.EqualTo(1));
                Assert.That(restored.Draft.SourceTranscriptionResume.Segments[0].Text,
                    Is.EqualTo("decoded source"));
                // The name the finished chunks were sent under. Without it a run
                // resumed in a later session would ask for chunks it has already
                // paid for as though they were new.
                Assert.That(
                    restored.Draft.SourceTranscriptionResume.RequestKeySeed,
                    Is.EqualTo("1f4c2b0d9e6a4f118b7c3d5e6f708192"));
                Assert.That(
                    restored.Draft.SourceTranscriptionResume.RequestKeyModel,
                    Is.EqualTo("openai/whisper-1"));
            });
        }
    }
}
