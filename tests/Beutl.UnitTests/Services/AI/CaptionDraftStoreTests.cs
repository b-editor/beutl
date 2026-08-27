using System.Globalization;
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
    public void Save_RoundTripsEveryRetainedPaidRecovery()
    {
        var store = new FileCaptionDraftStore(_storageDirectory);
        CaptionDraftScope scope = CreateScope();
        var retained = new CaptionDraftEntry(
            "job-a",
            CreateResumableSourceDraft());
        var current = new CaptionDraftEntry(
            "job-b",
            CreateResumableSourceDraft(),
            [retained]);
        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? session), Is.True);

        using (session)
        {
            session!.Save(current);
            CaptionDraftEntry restored = session.Read().Entry!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(restored.JobId, Is.EqualTo("job-b"));
                Assert.That(restored.Recoveries, Has.Length.EqualTo(1));
                Assert.That(restored.Recoveries[0].JobId, Is.EqualTo("job-a"));
                Assert.That(
                    restored.Recoveries[0].Draft.SourceTranscriptionResume!.RequestKeySeed,
                    Is.EqualTo(retained.Draft.SourceTranscriptionResume!.RequestKeySeed));
            }
        }
    }

    [Test]
    public void Save_RejectsMoreRecoveriesBeforeReplacingTheDurableEntry()
    {
        var store = new FileCaptionDraftStore(_storageDirectory);
        CaptionDraftScope scope = CreateScope();
        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? session), Is.True);

        using (session)
        {
            var original = new CaptionDraftEntry("job-original", CreateResumableSourceDraft());
            session!.Save(original);
            CaptionDraftEntry[] tooMany = Enumerable.Range(0, 64)
                .Select(index => new CaptionDraftEntry(
                    $"job-{index}",
                    CreateResumableSourceDraft()))
                .ToArray();

            Assert.Throws<ArgumentException>(() => session.Save(new CaptionDraftEntry(
                "job-new",
                CreateResumableSourceDraft(),
                tooMany)));
            Assert.That(session.Read().Entry!.JobId, Is.EqualTo("job-original"));
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
            CaptionDraftEntry? restored = session.Read().Entry;

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
            Assert.That(session.Read().Entry, Is.Null);
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
                Assert.That(session!.Read().Entry, Is.Null);
                Assert.That(File.Exists(storagePath), Is.False);
            });

            File.WriteAllBytes(
                storagePath,
                new byte[FileCaptionDraftStore.MaximumStorageBytes + 1]);
            Assert.Multiple(() =>
            {
                Assert.That(session!.Read().Entry, Is.Null);
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
                    Assert.That(other!.Read().Entry, Is.Null, scope.ToString());
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
        Assert.That(owner.Read().Entry, Is.Not.Null);

        owner.Dispose();
        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? successor), Is.True);
        using (successor)
        {
            Assert.That(successor!.Read().Entry?.Draft.Cues.Single().Text, Is.EqualTo("paid result"));
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
            CaptionDraftEntry? restored = restoredSession!.Read().Entry;
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
            CaptionDraftEntry? restored = session.Read().Entry;

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
    public void Load_KeepsWhatAnEarlierVersionPaidForAndDropsOnlyItsNames()
    {
        // 版 1 の控えには、seed を書いたあとモデルを書く前の時期のものが混じって
        // いる。seed だけがある状態は「モデルを指定しなかった実行」と見分けが
        // つかず、取り違えると支払い済みの切れ端に別の名前を付けて買い直す。
        // 曖昧なのは名前だけなので、支払い済みの結果まで捨てる理由は無い。
        var store = new FileCaptionDraftStore(_storageDirectory);
        CaptionDraftScope scope = CreateScope();
        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? session), Is.True);
        using (session)
        {
            session!.Save(new CaptionDraftEntry("job-1", CreateResumableSourceDraft()));
        }

        string path = store.GetStoragePath(scope);
        string stored = File.ReadAllText(path);
        File.WriteAllText(
            path,
            stored.Replace(
                $"\"version\":{FileCaptionDraftStore.CurrentVersion}",
                "\"version\":1"));

        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? reopened), Is.True);
        using (reopened)
        {
            CaptionDraftEntry? restored = reopened!.Read().Entry;

            Assert.That(restored, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(restored!.Draft.Cues.Single().Text, Is.EqualTo("paid result"));
                Assert.That(restored.Draft.CompletedSteps, Is.EqualTo(1));
                Assert.That(
                    restored.Draft.SourceTranscriptionResume?.RequestKeySeed,
                    Is.Empty,
                    "The names of a version 1 draft cannot be told apart, so they go.");
                Assert.That(
                    restored.Draft.SourceTranscriptionResume?.RequestKeyModel,
                    Is.Empty);
                Assert.That(
                    restored.Draft.SourceTranscriptionResume?.RequestKeyNamePending,
                    Is.False);
            });
        }
    }

    [Test]
    public void Load_ReadsAVersion2DraftAsStillHoldingItsName()
    {
        // 版 2 には「名前を抱えたまま終わったか」が無い。読み落として false に
        // すると、まだ返ってきていない最初の切れ端を持つ実行が拾い直せず、
        // 買い直しになる。版 2 は切れ端が返るたびに名前を決着させていなかった
        // ので、seed があるなら抱えていたということ。
        var store = new FileCaptionDraftStore(_storageDirectory);
        CaptionDraftScope scope = CreateScope();
        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? session), Is.True);
        using (session)
        {
            session!.Save(new CaptionDraftEntry("job-1", CreateResumableSourceDraft()));
        }

        string path = store.GetStoragePath(scope);
        string stored = File.ReadAllText(path);
        // 版 2 が書いた控えそのもの——版が 2 で、その項目がまだ無い。
        string asVersion2 = stored
            .Replace(
                $"\"version\":{FileCaptionDraftStore.CurrentVersion}",
                "\"version\":2")
            .Replace(",\"requestKeyNamePending\":true", string.Empty);
        Assert.That(asVersion2, Does.Not.Contain("requestKeyNamePending"));
        File.WriteAllText(path, asVersion2);

        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? reopened), Is.True);
        using (reopened)
        {
            CaptionDraftEntry? restored = reopened!.Read().Entry;

            Assert.That(restored, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(
                    restored!.Draft.SourceTranscriptionResume?.RequestKeySeed,
                    Is.EqualTo("seed-of-the-run"),
                    "What it paid for is still named.");
                Assert.That(
                    restored.Draft.SourceTranscriptionResume?.RequestKeyNamePending,
                    Is.True);
            });
        }
    }

    [Test]
    public void Read_SaysUnreadableForADraftANewerVersionWroteInsteadOfDeletingIt()
    {
        // 新しい版で書かれた控えを、古い版に戻して開いたとき。読めないだけで
        // 壊れてはいないので、消してはいけない——消すと、新しい版に戻っても
        // そこに書いてあった支払い済みの名前は返ってこない。
        var store = new FileCaptionDraftStore(_storageDirectory);
        CaptionDraftScope scope = CreateScope();
        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? session), Is.True);
        using (session)
        {
            session!.Save(new CaptionDraftEntry("job-1", CreateResumableSourceDraft()));
        }

        string path = store.GetStoragePath(scope);
        string stored = File.ReadAllText(path);
        string asFutureVersion = stored.Replace(
            string.Create(CultureInfo.InvariantCulture, $"\"version\":{FileCaptionDraftStore.CurrentVersion}"),
            string.Create(CultureInfo.InvariantCulture, $"\"version\":{FileCaptionDraftStore.CurrentVersion + 1}"));
        Assert.That(asFutureVersion, Is.Not.EqualTo(stored));
        File.WriteAllText(path, asFutureVersion);

        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? reopened), Is.True);
        using (reopened)
        {
            CaptionDraftReadResult read = reopened!.Read();
            Assert.Multiple(() =>
            {
                Assert.That(read.Outcome, Is.EqualTo(CaptionDraftReadOutcome.Unreadable));
                Assert.That(read.Entry, Is.Null);
            });
        }

        Assert.That(
            File.Exists(path),
            Is.True,
            "A draft a newer version wrote is still there for that version to read.");
    }

    [Test]
    public void TryOpen_RefusesAScopeAnotherProcessIsHolding()
    {
        // 同じ人の同じ場面を、もう 1 つの Beutl が開いていることがある。どちらも
        // 書けてしまうと、片方が書いた支払い済みの名前をもう片方が上書きし、次の
        // 起動でそれを買い直す。別の store インスタンスは別のプロセスの代わり。
        var first = new FileCaptionDraftStore(_storageDirectory);
        var second = new FileCaptionDraftStore(_storageDirectory);
        CaptionDraftScope scope = CreateScope();

        Assert.That(first.TryOpen(scope, out ICaptionDraftSession? held), Is.True);
        using (held)
        {
            Assert.That(second.TryOpen(scope, out ICaptionDraftSession? blocked), Is.False);
            Assert.That(blocked, Is.Null);
        }

        // 手放せば次の人が取れる。
        Assert.That(second.TryOpen(scope, out ICaptionDraftSession? afterRelease), Is.True);
        afterRelease!.Dispose();
    }

    [Test]
    public void Load_TakesAVersion2DraftAtItsWordWhenItSaysSo()
    {
        // 版 2 のうち、その項目を書くようになったあとのもの。書いてある false は
        // 「決着済み」という意味なので、抱えているものとして復活させてはいけない。
        var store = new FileCaptionDraftStore(_storageDirectory);
        CaptionDraftScope scope = CreateScope();
        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? session), Is.True);
        using (session)
        {
            session!.Save(new CaptionDraftEntry("job-1", CreateResumableSourceDraft()));
        }

        string path = store.GetStoragePath(scope);
        File.WriteAllText(
            path,
            File.ReadAllText(path)
                .Replace(
                    $"\"version\":{FileCaptionDraftStore.CurrentVersion}",
                    "\"version\":2")
                .Replace("\"requestKeyNamePending\":true", "\"requestKeyNamePending\":false"));

        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? reopened), Is.True);
        using (reopened)
        {
            CaptionDraftEntry? restored = reopened!.Read().Entry;

            Assert.That(restored, Is.Not.Null);
            Assert.That(
                restored!.Draft.SourceTranscriptionResume?.RequestKeyNamePending,
                Is.False,
                "A draft that says the run settled is not a paid recovery.");
        }
    }

    [Test]
    public void Read_SaysUnreadableRatherThanDeletingWhatItCannotOpen()
    {
        // 読めなかったのと、無いのとは違う。取り違えて消すか上書きすると、
        // そこに書いてあった支払い済みの名前ごと失われる。
        var store = new FileCaptionDraftStore(_storageDirectory);
        CaptionDraftScope scope = CreateScope();
        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? session), Is.True);
        using (session)
        {
            session!.Save(new CaptionDraftEntry("job-1", CreateResumableSourceDraft()));
        }

        string path = store.GetStoragePath(scope);
        using (FileStream held = new(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.That(store.TryOpen(scope, out ICaptionDraftSession? blocked), Is.True);
            using (blocked)
            {
                CaptionDraftReadResult read = blocked!.Read();
                Assert.That(read.Outcome, Is.EqualTo(CaptionDraftReadOutcome.Unreadable));
            }
        }

        Assert.That(File.Exists(path), Is.True, "What could not be read is still there.");
        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? reopened), Is.True);
        using (reopened)
        {
            Assert.That(reopened!.Read().Entry, Is.Not.Null);
        }
    }

    private static CaptionDraft CreateResumableSourceDraft()
    {
        var cue = new StoredCaptionCue(
            TimeSpan.Zero.Ticks,
            TimeSpan.FromSeconds(1).Ticks,
            "paid result",
            null,
            "en",
            new Dictionary<string, string>(StringComparer.Ordinal));
        var segment = new AiTranscriptionSegment { Start = 0, End = 1, Text = "paid result" };
        return new CaptionDraft(
            FileCaptionDraftStore.CurrentVersion,
            [cue],
            "en",
            [segment],
            CaptionDraftKind.Transcription,
            1,
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
                [segment],
                "en",
                1,
                "seed-of-the-run",
                "openai/whisper-1",
                true));
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
                "openai/gpt-5",
                false,
                150,
                12_000,
                96 * 1024),
            null);

        Assert.That(store.TryOpen(scope, out ICaptionDraftSession? session), Is.True);
        using (session)
        {
            session!.Save(new CaptionDraftEntry("job-translation", draft));
        }

        Assert.That(store.TryOpen(scope, out session), Is.True);
        using (session)
        {
            CaptionDraftEntry? restored = session!.Read().Entry;
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
                Assert.That(restored.Draft.TranslationResume.MaxSegments, Is.EqualTo(150));
                Assert.That(restored.Draft.TranslationResume.MaxCharacters, Is.EqualTo(12_000));
                Assert.That(restored.Draft.TranslationResume.MaxRequestBytes,
                    Is.EqualTo(96 * 1024));
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
            CaptionDraftEntry? restored = session!.Read().Entry;
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
