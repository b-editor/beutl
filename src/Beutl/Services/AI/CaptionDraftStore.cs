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
    int CompletedBatchCount);

internal sealed record CaptionSceneTranscriptionResume(
    Guid SceneId,
    string StartText,
    string EndText,
    string? Language,
    TimeSpan RangeStart,
    TimeSpan Duration,
    TimeSpan ChunkDuration,
    int ChunkCount,
    AiTranscriptionSegment[] Segments,
    string? DetectedLanguage,
    int CompletedChunkCount);

internal sealed record CaptionDraft(
    int Version,
    StoredCaptionCue[] Cues,
    string? Language,
    AiTranscriptionSegment[]? Segments,
    CaptionDraftKind Kind,
    int CompletedSteps,
    int TotalSteps,
    CaptionTranslationResume? TranslationResume,
    CaptionSceneTranscriptionResume? SceneTranscriptionResume);

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

internal interface ICaptionDraftSession : IDisposable
{
    CaptionDraftScope Scope { get; }

    CaptionDraftEntry? Load();

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
    internal const int CurrentVersion = 1;
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

            Guid leaseId = Guid.NewGuid();
            _leases.Add(scope, leaseId);
            session = new Session(this, scope, leaseId);
            return true;
        }
    }

    internal string GetStoragePath(CaptionDraftScope scope)
    {
        byte[] identity = JsonSerializer.SerializeToUtf8Bytes(scope, s_jsonOptions);
        string fileName = Convert.ToHexString(SHA256.HashData(identity)) + ".json";
        return Path.Combine(StorageDirectory, fileName);
    }

    private CaptionDraftEntry? Load(CaptionDraftScope scope, Guid leaseId)
    {
        lock (_gate)
        {
            EnsureLease(scope, leaseId);
            string storagePath = GetStoragePath(scope);
            DeleteStaleTemporaryFiles(storagePath);
            if (!File.Exists(storagePath))
                return null;

            try
            {
                var info = new FileInfo(storagePath);
                if (info.Length is <= 0 or > MaximumStorageBytes)
                {
                    DeleteInvalidFile(storagePath);
                    return null;
                }

                byte[] bytes = File.ReadAllBytes(storagePath);
                CaptionDraftEnvelope? envelope = JsonSerializer.Deserialize<CaptionDraftEnvelope>(
                    bytes,
                    s_jsonOptions);
                if (envelope is null
                    || envelope.Version != CurrentVersion
                    || envelope.Scope != scope
                    || !IsValid(envelope.Draft))
                {
                    DeleteInvalidFile(storagePath);
                    return null;
                }
                return new CaptionDraftEntry(envelope.JobId, envelope.Draft);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException
                or ArgumentException)
            {
                DeleteInvalidFile(storagePath);
                return null;
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
            || draft.CompletedSteps <= 0
            || draft.TotalSteps < draft.CompletedSteps)
        {
            return false;
        }

        return draft.Kind switch
        {
            CaptionDraftKind.Translation => draft.Cues.Length > 0
                && IsValid(draft.TranslationResume)
                && draft.SceneTranscriptionResume is null,
            CaptionDraftKind.Transcription => IsValidTranscriptionDraft(draft),
            _ => false,
        };
    }

    private static bool IsValidTranscriptionDraft(CaptionDraft draft)
        => draft.TranslationResume is null
            && draft.Segments is not null
            && (IsValid(draft.SceneTranscriptionResume)
                || draft.SceneTranscriptionResume is null
                && draft.CompletedSteps == draft.TotalSteps);

    private static bool IsValid(CaptionTranslationResume? resume)
        => resume is
        {
            SourceCues.Length: > 0 and <= MaximumCueCount,
            CompletedBatchCount: > 0,
        }
            && !string.IsNullOrWhiteSpace(resume.TargetLanguage)
            && resume.SourceCues.All(cue => cue is not null
                && cue.Text is not null
                && cue.Text.Length <= MaximumCueTextLength
                && cue.StartTicks >= 0
                && cue.EndTicks > cue.StartTicks
                && cue.Metadata is not null)
            && resume.TranslatedPieces is { Count: > 0 }
            && resume.TranslatedPieces.All(pair =>
                !string.IsNullOrWhiteSpace(pair.Key)
                && !string.IsNullOrWhiteSpace(pair.Value)
                && pair.Value.Length <= MaximumCueTextLength);

    private static bool IsValid(CaptionSceneTranscriptionResume? resume)
        => resume is
        {
            ChunkCount: > 0,
            CompletedChunkCount: > 0,
            Duration: { } duration,
            ChunkDuration: { } chunkDuration,
            Segments: not null,
        }
            && resume.CompletedChunkCount <= resume.ChunkCount
            && resume.SceneId != Guid.Empty
            && !string.IsNullOrWhiteSpace(resume.StartText)
            && !string.IsNullOrWhiteSpace(resume.EndText)
            && duration > TimeSpan.Zero
            && chunkDuration > TimeSpan.Zero
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
        Guid leaseId) : ICaptionDraftSession
    {
        private FileCaptionDraftStore? _owner = owner;

        public CaptionDraftScope Scope { get; } = scope;

        public CaptionDraftEntry? Load()
            => GetOwner().Load(Scope, leaseId);

        public void Save(CaptionDraftEntry entry)
            => GetOwner().Save(Scope, leaseId, entry);

        public void Delete()
            => GetOwner().Delete(Scope, leaseId);

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.Release(Scope, leaseId);

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
