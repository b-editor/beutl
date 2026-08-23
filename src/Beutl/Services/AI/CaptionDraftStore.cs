using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.Api.Services;
using Beutl.ProjectSystem;

namespace Beutl.Services.AI;

internal enum CaptionDraftKind
{
    Translation,
    Transcription,
}

internal sealed record StoredCaptionCue(
    long StartTicks,
    long EndTicks,
    string Text,
    string? Speaker,
    string? Language,
    Dictionary<string, string> Metadata);

internal sealed record CaptionTranslationResume(
    StoredCaptionCue[] SourceCues,
    string? SourceLanguage,
    string? SelectedSourceLanguage,
    string TargetLanguage,
    Dictionary<string, string> TranslatedPieces,
    int CompletedBatchCount,
    // What the batches of this run were named to the server. Kept so that a run
    // resumed in a later session asks for the translations it already paid for
    // rather than buying them again. Empty in a draft written before this was
    // recorded, which resumes as a run of its own.
    string RequestKeySeed = "",
    // The model those names were built from. A run resumed on another model
    // would name its unfinished batches differently and buy them again, so the
    // picker is put back on this one.
    string RequestKeyModel = "",
    // 送ったきり決着していない名前を抱えたまま終わったか。seed があることでは
    // 分からない——予約されなかった依頼や、返金されて捨てられた依頼の seed も
    // 残る。これを取り違えると、誰も払っていない依頼が「支払い済みの回収」と
    // して復活する。
    bool RequestKeyNamePending = false);

internal sealed record CaptionSceneTranscriptionResume(
    Guid SceneId,
    string? Language,
    TimeSpan RangeStart,
    TimeSpan Duration,
    TimeSpan ChunkDuration,
    int ChunkCount,
    AiTranscriptionSegment[] Segments,
    string? DetectedLanguage,
    int CompletedChunkCount,
    // What the chunks of this run were named to the server. Scene audio is
    // composed rather than read from a file, so nothing written down proves it
    // is still the same audio — the server's own fingerprint does. A chunk
    // asked for again under this seed is answered from the job it made when the
    // audio matches, and refused as a different request when it does not, which
    // is where the run starts over.
    string RequestKeySeed = "",
    string RequestKeyModel = "",
    bool RequestKeyNamePending = false);

internal sealed record CaptionSourceTranscriptionResume(
    string FilePath,
    Guid ElementId,
    long FileLength,
    long LastWriteTimeUtcTicks,
    string? Language,
    int SampleRate,
    long TotalSamples,
    int ChunkSamples,
    int ChunkCount,
    AiTranscriptionSegment[] Segments,
    string? DetectedLanguage,
    int CompletedChunkCount,
    // What the chunks of this run were named to the server. Kept so that a run
    // resumed in a later session asks for the transcriptions it already paid
    // for rather than buying them again. Empty in a draft written before this
    // was recorded, which resumes as a run of its own.
    string RequestKeySeed = "",
    // The model those names were built from. A run resumed on another model
    // would name its unfinished chunks differently and buy them again, so the
    // picker is put back on this one.
    string RequestKeyModel = "",
    // 送ったきり決着していない名前を抱えたまま終わったか。seed があることでは
    // 分からない——予約されなかった依頼や、返金されて捨てられた依頼の seed も
    // 残る。
    bool RequestKeyNamePending = false);

internal sealed record CaptionDraft(
    int Version,
    StoredCaptionCue[] Cues,
    string? Language,
    AiTranscriptionSegment[]? Segments,
    CaptionDraftKind Kind,
    int CompletedSteps,
    int TotalSteps,
    CaptionTranslationResume? TranslationResume,
    CaptionSceneTranscriptionResume? SceneTranscriptionResume,
    CaptionSourceTranscriptionResume? SourceTranscriptionResume = null);

internal sealed record CaptionDraftScope
{
    [JsonConstructor]
    public CaptionDraftScope(
        string userId,
        Guid projectId,
        Guid sceneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (projectId == Guid.Empty)
            throw new ArgumentException("A project identifier is required.", nameof(projectId));
        if (sceneId == Guid.Empty)
            throw new ArgumentException("A scene identifier is required.", nameof(sceneId));

        string normalizedUserId = userId.Trim();
        if (normalizedUserId.Length > 256)
            throw new ArgumentException("The user identifier is too long.", nameof(userId));

        UserId = normalizedUserId;
        ProjectId = projectId;
        SceneId = sceneId;
    }

    public string UserId { get; }

    public Guid ProjectId { get; }

    public Guid SceneId { get; }
}

internal sealed record CaptionDraftEntry
{
    public CaptionDraftEntry(string? jobId, CaptionDraft draft)
    {
        string? normalizedJobId = string.IsNullOrWhiteSpace(jobId) ? null : jobId.Trim();
        if (normalizedJobId?.Length > 256)
            throw new ArgumentException("The job identifier is too long.", nameof(jobId));

        JobId = normalizedJobId;
        Draft = draft ?? throw new ArgumentNullException(nameof(draft));
    }

    public string? JobId { get; }

    public CaptionDraft Draft { get; }
}

/// <summary>What came of looking for a scope's draft.</summary>
internal enum CaptionDraftReadOutcome
{
    /// <summary>Nothing is stored for this scope, and nothing was.</summary>
    Absent,

    /// <summary>
    /// Something may be stored and could not be read. Never the same as absent:
    /// writing over what could not be read destroys whatever it held, which may
    /// be the only way back to something already paid for.
    /// </summary>
    Unreadable,

    /// <summary>A draft was read.</summary>
    Read,
}

internal readonly record struct CaptionDraftReadResult(
    CaptionDraftReadOutcome Outcome,
    CaptionDraftEntry? Entry)
{
    public static readonly CaptionDraftReadResult Absent = new(CaptionDraftReadOutcome.Absent, null);

    public static readonly CaptionDraftReadResult Unreadable =
        new(CaptionDraftReadOutcome.Unreadable, null);
}

internal interface ICaptionDraftSession : IDisposable
{
    CaptionDraftScope Scope { get; }

    CaptionDraftReadResult Read();

    void Save(CaptionDraftEntry entry);

    void Delete();
}

internal interface ICaptionDraftStore
{
    bool TryOpen(
        CaptionDraftScope scope,
        [NotNullWhen(true)] out ICaptionDraftSession? session);
}

internal sealed class FileCaptionDraftStore : ICaptionDraftStore
{
    // Version 2 records what a run's requests were named before its first piece
    // comes back; version 3 also records whether one of those names was still
    // outstanding when the draft was written.
    internal const int CurrentVersion = 3;
    // 古い控えも読む。曖昧なのはそのときどきの名前まわりだけなので、そこだけを
    // 直して読み込む——支払い済みの結果まで捨てる理由は無い。
    internal const int OldestSupportedVersion = 1;
    internal const int MaximumStorageBytes = 8 * 1024 * 1024;
    private const int MaximumCueCount = 10_000;
    private const int MaximumCueTextLength = 100_000;

    private static readonly JsonSerializerOptions s_jsonOptions = CreateJsonOptions();
    private readonly Dictionary<CaptionDraftScope, Guid> _leases = [];
    private readonly object _gate = new();

    public FileCaptionDraftStore(string storageDirectory)
    {
        if (string.IsNullOrWhiteSpace(storageDirectory))
            throw new ArgumentException("A storage directory is required.", nameof(storageDirectory));

        StorageDirectory = Path.GetFullPath(storageDirectory);
        if (File.Exists(StorageDirectory))
            throw new ArgumentException("The storage path must identify a directory.", nameof(storageDirectory));
    }

    public string StorageDirectory { get; }

    public bool TryOpen(
        CaptionDraftScope scope,
        [NotNullWhen(true)] out ICaptionDraftSession? session)
    {
        ArgumentNullException.ThrowIfNull(scope);
        lock (_gate)
        {
            if (_leases.ContainsKey(scope))
            {
                session = null;
                return false;
            }

            // 同じ人の同じ場面を、もう 1 つの Beutl が開いていることがある。
            // どちらも書けてしまうと、片方が書いた「支払い済みの名前」をもう
            // 片方が上書きし、次の起動でそれを買い直すことになる。ファイルを
            // 共有無しで開いたまま持ち、開けたほうだけが書く。
            FileStream? lockFile = TryTakeLockFile(scope);
            if (lockFile is null)
            {
                session = null;
                return false;
            }

            Guid leaseId = Guid.NewGuid();
            _leases.Add(scope, leaseId);
            session = new Session(this, scope, leaseId, lockFile);
            return true;
        }
    }

    private FileStream? TryTakeLockFile(CaptionDraftScope scope)
    {
        try
        {
            Directory.CreateDirectory(StorageDirectory);
            return new FileStream(
                GetStoragePath(scope) + ".lock",
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            // 他のプロセスが握っている。
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal string GetStoragePath(CaptionDraftScope scope)
    {
        byte[] identity = JsonSerializer.SerializeToUtf8Bytes(scope, s_jsonOptions);
        string fileName = Convert.ToHexString(SHA256.HashData(identity)) + ".json";
        return Path.Combine(StorageDirectory, fileName);
    }

    private CaptionDraftReadResult Read(CaptionDraftScope scope, Guid leaseId)
    {
        lock (_gate)
        {
            EnsureLease(scope, leaseId);
            string storagePath = GetStoragePath(scope);
            DeleteStaleTemporaryFiles(storagePath);
            if (!File.Exists(storagePath))
                return CaptionDraftReadResult.Absent;

            byte[] bytes;
            try
            {
                var info = new FileInfo(storagePath);
                if (info.Length is <= 0 or > MaximumStorageBytes)
                {
                    DeleteInvalidFile(storagePath);
                    return CaptionDraftReadResult.Absent;
                }

                bytes = File.ReadAllBytes(storagePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 読めなかっただけで、無いとは限らない。消しても上書きもしない
                // ——そこに支払い済みの名前が書いてあるかもしれない。
                return CaptionDraftReadResult.Unreadable;
            }

            try
            {
                CaptionDraftEnvelope? envelope = JsonSerializer.Deserialize<CaptionDraftEnvelope>(
                    bytes,
                    s_jsonOptions);
                // 新しい版で書かれた控え。読めないだけで、壊れてはいない
                // ——古い版へ戻したときに消してしまうと、新しい版に戻っても
                // 支払い済みの名前は返ってこない。
                if (envelope is not null && envelope.Version > CurrentVersion)
                    return CaptionDraftReadResult.Unreadable;

                if (envelope is null
                    || envelope.Version < OldestSupportedVersion
                    || envelope.Scope != scope)
                {
                    DeleteInvalidFile(storagePath);
                    return CaptionDraftReadResult.Absent;
                }

                CaptionDraft draft = Migrate(
                    envelope.Draft,
                    envelope.Version,
                    RecordsNamePending(bytes, envelope.Version));
                if (!IsValid(draft))
                {
                    DeleteInvalidFile(storagePath);
                    return CaptionDraftReadResult.Absent;
                }
                return new CaptionDraftReadResult(
                    CaptionDraftReadOutcome.Read,
                    new CaptionDraftEntry(envelope.JobId, draft));
            }
            catch (Exception ex) when (ex is JsonException
                or NotSupportedException
                or ArgumentException)
            {
                // 読めたが、中身が控えとして成り立っていない。これは消してよい。
                DeleteInvalidFile(storagePath);
                return CaptionDraftReadResult.Absent;
            }
        }
    }

    private void Save(CaptionDraftScope scope, Guid leaseId, CaptionDraftEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!IsValid(entry.Draft))
            throw new ArgumentException("The caption draft is invalid.", nameof(entry));

        lock (_gate)
        {
            EnsureLease(scope, leaseId);
            string storagePath = GetStoragePath(scope);
            DeleteStaleTemporaryFiles(storagePath);
            var envelope = new CaptionDraftEnvelope(
                CurrentVersion,
                scope,
                entry.JobId,
                entry.Draft);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, s_jsonOptions);
            if (bytes.Length > MaximumStorageBytes)
                throw new InvalidOperationException("The caption draft exceeds the storage limit.");

            Directory.CreateDirectory(StorageDirectory);
            string temporaryPath = storagePath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllBytes(temporaryPath, bytes);
                RestrictFileAccess(temporaryPath);
                File.Move(temporaryPath, storagePath, overwrite: true);
                RestrictFileAccess(storagePath);
            }
            finally
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private void Delete(CaptionDraftScope scope, Guid leaseId)
    {
        lock (_gate)
        {
            EnsureLease(scope, leaseId);
            File.Delete(GetStoragePath(scope));
        }
    }

    private void Release(CaptionDraftScope scope, Guid leaseId)
    {
        lock (_gate)
        {
            if (_leases.TryGetValue(scope, out Guid currentLease) && currentLease == leaseId)
            {
                _leases.Remove(scope);
            }
        }
    }

    private void EnsureLease(CaptionDraftScope scope, Guid leaseId)
    {
        if (!_leases.TryGetValue(scope, out Guid currentLease) || currentLease != leaseId)
            throw new InvalidOperationException("The caption draft session no longer owns this scope.");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false));
        return options;
    }

    // 版 1 の控えには、seed を書いたあとモデルを書く前の時期のものが混じって
    // いる。seed だけがある状態は「モデルを指定しなかった実行」と見分けがつかず、
    // 取り違えると、支払い済みの切れ端に別の名前を付けて買い直すことになる。
    //
    // 曖昧なのは名前だけなので、名前だけを落とす。支払い済みの結果はそのまま
    // 残り、まだ送っていない残りの切れ端が新しい実行として値付けされる。
    // その控えが「名前を抱えたまま終わったか」を実際に書いているか。版 2 には
    // それを書く前のものと書くようになったあとのものが混じっていて、書いてある
    // false と、書かれていないための false は、読んだだけでは同じに見える。
    private static bool RecordsNamePending(byte[] bytes, int version)
    {
        if (version != 2)
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            if (!document.RootElement.TryGetProperty("draft", out JsonElement draft))
                return false;

            foreach (string resume in
                (string[])["translationResume", "sourceTranscriptionResume", "sceneTranscriptionResume"])
            {
                if (draft.TryGetProperty(resume, out JsonElement element)
                    && element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("requestKeyNamePending", out _))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static CaptionDraft Migrate(CaptionDraft draft, int version, bool recordsNamePending)
    {
        if (version >= CurrentVersion)
            return draft;

        // 書いてあるなら、そのとおりに読む。書いていないのは、その項目より前の
        // 版 2——seed があるなら抱えていたということ。
        if (version == 2 && recordsNamePending)
            return draft with { Version = CurrentVersion };

        // 版 2 には「名前を抱えたまま終わったか」が無い。読み落とすと false に
        // なり、まだ返ってきていない最初の切れ端を持つ実行が拾い直せなくなって
        // 買い直しになる。版 2 は切れ端が返るたびに名前を決着させていなかった
        // ので、seed があるなら抱えていたということ——そう読む。取り違えて
        // true にしても、サーバーがその名前で何も見つけなければ普通に課金される
        // だけで、失うものは無い。
        if (version == 2)
        {
            return draft with
            {
                Version = CurrentVersion,
                TranslationResume = draft.TranslationResume is { } pendingTranslation
                    ? pendingTranslation with
                    {
                        RequestKeyNamePending =
                            !string.IsNullOrEmpty(pendingTranslation.RequestKeySeed),
                    }
                    : null,
                SourceTranscriptionResume =
                    draft.SourceTranscriptionResume is { } pendingSource
                        ? pendingSource with
                        {
                            RequestKeyNamePending =
                                !string.IsNullOrEmpty(pendingSource.RequestKeySeed),
                        }
                        : null,
                SceneTranscriptionResume =
                    draft.SceneTranscriptionResume is { } pendingScene
                        ? pendingScene with
                        {
                            RequestKeyNamePending =
                                !string.IsNullOrEmpty(pendingScene.RequestKeySeed),
                        }
                        : null,
            };
        }

        return draft with
        {
            Version = CurrentVersion,
            TranslationResume = draft.TranslationResume is { } translation
                ? translation with
                {
                    RequestKeySeed = string.Empty,
                    RequestKeyModel = string.Empty,
                    RequestKeyNamePending = false,
                }
                : null,
            SourceTranscriptionResume = draft.SourceTranscriptionResume is { } source
                ? source with
                {
                    RequestKeySeed = string.Empty,
                    RequestKeyModel = string.Empty,
                    RequestKeyNamePending = false,
                }
                : null,
            SceneTranscriptionResume = draft.SceneTranscriptionResume is { } scene
                ? scene with
                {
                    RequestKeySeed = string.Empty,
                    RequestKeyModel = string.Empty,
                    RequestKeyNamePending = false,
                }
                : null,
        };
    }

    private static bool IsValid(CaptionDraft draft)
    {
        if (draft.Version != CurrentVersion
            || draft.Cues is null
            || draft.Cues.Length > MaximumCueCount
            || draft.Cues.Any(cue => cue is null
                || cue.Text is null
                || cue.Text.Length > MaximumCueTextLength
                || cue.StartTicks < 0
                || cue.EndTicks <= cue.StartTicks
                || cue.Metadata is null
                || cue.Metadata.Any(pair =>
                    string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null))
            // Nothing finished yet is a run worth keeping: it holds the names
            // its first pieces were sent under, which is what makes them
            // collectable rather than something to buy a second time.
            || draft.CompletedSteps < 0
            || draft.TotalSteps < draft.CompletedSteps)
        {
            return false;
        }

        return draft.Kind switch
        {
            CaptionDraftKind.Translation => draft.Cues.Length > 0
                && IsValid(draft.TranslationResume)
                && draft.SceneTranscriptionResume is null
                && draft.SourceTranscriptionResume is null,
            CaptionDraftKind.Transcription => IsValidTranscriptionDraft(draft),
            _ => false,
        };
    }

    private static bool IsValidTranscriptionDraft(CaptionDraft draft)
        => draft.TranslationResume is null
            && draft.Segments is not null
            && (IsValid(draft.SceneTranscriptionResume)
                && draft.SourceTranscriptionResume is null
                || IsValid(draft.SourceTranscriptionResume)
                && draft.SceneTranscriptionResume is null
                || draft.SceneTranscriptionResume is null
                && draft.SourceTranscriptionResume is null
                && draft.CompletedSteps == draft.TotalSteps);

    private static bool IsValid(CaptionTranslationResume? resume)
        => resume is
        {
            SourceCues.Length: > 0 and <= MaximumCueCount,
            CompletedBatchCount: >= 0,
        }
            && !string.IsNullOrWhiteSpace(resume.TargetLanguage)
            && resume.SourceCues.All(cue => cue is not null
                && cue.Text is not null
                && cue.Text.Length <= MaximumCueTextLength
                && cue.StartTicks >= 0
                && cue.EndTicks > cue.StartTicks
                && cue.Metadata is not null)
            && resume.TranslatedPieces is not null
            && (resume.CompletedBatchCount > 0) == (resume.TranslatedPieces.Count > 0)
            && resume.TranslatedPieces.All(pair =>
                !string.IsNullOrWhiteSpace(pair.Key)
                && !string.IsNullOrWhiteSpace(pair.Value)
                && pair.Value.Length <= MaximumCueTextLength);

    private static bool IsValid(CaptionSceneTranscriptionResume? resume)
        => resume is
        {
            ChunkCount: > 0,
            CompletedChunkCount: >= 0,
            Duration: { } duration,
            ChunkDuration: { } chunkDuration,
            Segments: not null,
        }
            && resume.CompletedChunkCount <= resume.ChunkCount
            && resume.SceneId != Guid.Empty
            && duration > TimeSpan.Zero
            && chunkDuration > TimeSpan.Zero
            && resume.Segments.All(segment => segment is not null
                && double.IsFinite(segment.Start)
                && double.IsFinite(segment.End)
                && segment.End > segment.Start
                && segment.Text is not null
                && segment.Text.Length <= MaximumCueTextLength);

    private static bool IsValid(CaptionSourceTranscriptionResume? resume)
        => resume is
        {
            SampleRate: > 0,
            TotalSamples: > 0 and <= int.MaxValue,
            ChunkSamples: > 0,
            ChunkCount: > 0,
            CompletedChunkCount: >= 0,
            Segments: not null,
        }
            && resume.CompletedChunkCount < resume.ChunkCount
            && resume.TotalSamples > (long)resume.ChunkSamples
                * resume.CompletedChunkCount
            && resume.ChunkCount == checked((int)Math.Ceiling(
                resume.TotalSamples / (double)resume.ChunkSamples))
            && !string.IsNullOrWhiteSpace(resume.FilePath)
            && Path.IsPathFullyQualified(resume.FilePath)
            && resume.FileLength > 0
            && resume.LastWriteTimeUtcTicks >= 0
            && resume.LastWriteTimeUtcTicks <= DateTime.MaxValue.Ticks
            && resume.Segments.All(segment => segment is not null
                && double.IsFinite(segment.Start)
                && double.IsFinite(segment.End)
                && segment.End > segment.Start
                && segment.Text is not null
                && segment.Text.Length <= MaximumCueTextLength);

    private static void DeleteInvalidFile(string storagePath)
    {
        try
        {
            File.Delete(storagePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteStaleTemporaryFiles(string storagePath)
    {
        string? directory = Path.GetDirectoryName(storagePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;

        string pattern = Path.GetFileName(storagePath) + ".*.tmp";
        try
        {
            foreach (string path in Directory.EnumerateFiles(directory, pattern))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddHours(-1))
                        continue;
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void RestrictFileAccess(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (PlatformNotSupportedException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    private sealed record CaptionDraftEnvelope(
        int Version,
        CaptionDraftScope Scope,
        string? JobId,
        CaptionDraft Draft);

    private sealed class Session(
        FileCaptionDraftStore owner,
        CaptionDraftScope scope,
        Guid leaseId,
        FileStream lockFile) : ICaptionDraftSession
    {
        private FileCaptionDraftStore? _owner = owner;

        public CaptionDraftScope Scope { get; } = scope;

        public CaptionDraftReadResult Read()
            => GetOwner().Read(Scope, leaseId);

        public void Save(CaptionDraftEntry entry)
            => GetOwner().Save(Scope, leaseId, entry);

        public void Delete()
            => GetOwner().Delete(Scope, leaseId);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owner, null) is not { } owner)
                return;

            owner.Release(Scope, leaseId);
            try
            {
                lockFile.Dispose();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private FileCaptionDraftStore GetOwner()
            => Volatile.Read(ref _owner)
                ?? throw new ObjectDisposedException(nameof(ICaptionDraftSession));
    }
}

internal static class CaptionDraftStoreProvider
{
    private static readonly Lazy<ICaptionDraftStore> s_current = new(() =>
        new FileCaptionDraftStore(Path.Combine(
            BeutlEnvironment.GetHomeDirectoryPath(),
            "ai-caption-drafts")));
    private static ICaptionDraftStore? s_override;

    public static ICaptionDraftStore Current => Volatile.Read(ref s_override) ?? s_current.Value;

    internal static void SetCurrentForTesting(ICaptionDraftStore? store)
        => Volatile.Write(ref s_override, store);
}
