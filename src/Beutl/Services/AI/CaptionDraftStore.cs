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
    // Whether the draft ended while holding a dispatched but unsettled name. A seed alone cannot
    // answer this because unreserved and refunded requests also retain seeds. Confusing them would
    // restore unpaid work as an already-paid recovery.
    bool RequestKeyNamePending = false,
    int MaxSegments = 0,
    int MaxCharacters = 0,
    int MaxRequestBytes = 0);

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
    // Whether the draft ended while holding a dispatched but unsettled name. A seed alone cannot
    // answer this because unreserved and refunded requests also retain seeds.
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
    public CaptionDraftEntry(
        string? jobId,
        CaptionDraft draft,
        CaptionDraftEntry[]? recoveries = null)
    {
        string? normalizedJobId = string.IsNullOrWhiteSpace(jobId) ? null : jobId.Trim();
        if (normalizedJobId?.Length > 256)
            throw new ArgumentException("The job identifier is too long.", nameof(jobId));

        JobId = normalizedJobId;
        Draft = draft ?? throw new ArgumentNullException(nameof(draft));
        Recoveries = recoveries ?? [];
    }

    public string? JobId { get; }

    public CaptionDraft Draft { get; }

    public CaptionDraftEntry[] Recoveries { get; }
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
    // comes back; version 3 records whether one of those names was outstanding;
    // version 4 retains more than one paid recovery for a scene.
    internal const int CurrentVersion = 5;
    // Read old drafts too. Only their request-name fields are ambiguous, so normalize those fields
    // rather than discarding already-paid results.
    internal const int OldestSupportedVersion = 1;
    internal const int MaximumStorageBytes = 8 * 1024 * 1024;
    internal const int MaximumRetainedRecoveries = 63;
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

            // Another Beutl process may open the same scene for the same account. If both write,
            // one can overwrite the other's paid request name and force a repurchase on restart.
            // Keep the file open without sharing so only the process that acquired it may write.
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
            // Another process owns the draft.
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
                // Unreadable is not absent. Do not delete or overwrite a draft that may contain
                // an already-paid request name.
                return CaptionDraftReadResult.Unreadable;
            }

            try
            {
                CaptionDraftEnvelope? envelope = JsonSerializer.Deserialize<CaptionDraftEnvelope>(
                    bytes,
                    s_jsonOptions);
                // A newer version wrote this draft. It is unreadable, not corrupt; deleting it
                // after downgrading would lose paid request names even after upgrading again.
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
                CaptionDraftEntry[] recoveries = envelope.Recoveries ?? [];
                var entry = new CaptionDraftEntry(envelope.JobId, draft, recoveries);
                if (!IsValid(entry))
                {
                    DeleteInvalidFile(storagePath);
                    return CaptionDraftReadResult.Absent;
                }
                return new CaptionDraftReadResult(
                    CaptionDraftReadOutcome.Read,
                    entry);
            }
            catch (Exception ex) when (ex is JsonException
                or NotSupportedException
                or ArgumentException)
            {
                // The document was readable but is not a valid draft, so it may be removed.
                DeleteInvalidFile(storagePath);
                return CaptionDraftReadResult.Absent;
            }
        }
    }

    private void Save(CaptionDraftScope scope, Guid leaseId, CaptionDraftEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!IsValid(entry))
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
                entry.Draft,
                entry.Recoveries);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, s_jsonOptions);
            if (bytes.Length > MaximumStorageBytes)
                throw new InvalidOperationException("The caption draft exceeds the storage limit.");

            if (OperatingSystem.IsWindows())
                Directory.CreateDirectory(StorageDirectory);
            else
                Directory.CreateDirectory(
                    StorageDirectory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string temporaryPath = storagePath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                var options = new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.WriteThrough,
                };
                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (FileStream stream = new(temporaryPath, options))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
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

    // Version 1 spans the period after seeds were stored but before models were stored. A seed
    // without a model is indistinguishable from a request that intentionally omitted the model;
    // guessing wrong would rename and repurchase already-paid chunks.
    //
    // Only the name is ambiguous, so drop the name while preserving paid results; unsent chunks
    // are then priced as new work. This flag records whether the draft actually stored its
    // pending-name state. Version 2 contains documents from before and after that field was added,
    // and a stored false is otherwise indistinguishable from a missing false.
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

        if (version >= 3)
            return draft with { Version = CurrentVersion };

        // Honor an explicitly stored value. A missing value came from version 2 before the field
        // existed, where a seed means the draft held a name.
        if (version == 2 && recordsNamePending)
            return draft with { Version = CurrentVersion };

        // Version 2 did not store whether a name remained pending. Reading that as false would
        // make a run with an unanswered first chunk unrecoverable and charge again. Version 2 also
        // did not settle names after each returned chunk, so a seed is treated as a held name. If
        // that guess is too cautious and the server finds nothing, it simply handles a normal new charge.
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

    private static bool IsValid(CaptionDraftEntry entry)
        => IsValid(entry.Draft)
            && entry.Recoveries is { Length: <= MaximumRetainedRecoveries }
            && entry.Recoveries.All(recovery => recovery is not null
                && recovery.Recoveries.Length == 0
                && IsValid(recovery.Draft));

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
            && (resume.MaxSegments == 0
                && resume.MaxCharacters == 0
                && resume.MaxRequestBytes == 0
                || resume.MaxSegments > 0
                && resume.MaxCharacters > 0
                && resume.MaxRequestBytes > 0)
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
            && resume.CompletedChunkCount <= resume.ChunkCount
            && (resume.CompletedChunkCount == resume.ChunkCount
                || resume.TotalSamples > (long)resume.ChunkSamples
                    * resume.CompletedChunkCount)
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
        CaptionDraft Draft,
        CaptionDraftEntry[]? Recoveries = null);

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
