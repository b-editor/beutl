using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.Services.AI;

internal partial class PersistentPromptLibrary
{
    private static readonly JsonSerializerOptions s_jsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false));
        return options;
    }

    private void Load()
    {
        try
        {
            StorageDocument document;
            using (FileStream stream = File.OpenRead(StoragePath))
            {
                document = ReadStorageDocument(stream);
            }

            if (document.Version > CurrentStorageVersion)
            {
                throw new NotSupportedException(
                    $"Prompt library version {document.Version} is newer than supported version {CurrentStorageVersion}.");
            }

            if (document.Version < 1)
            {
                throw new InvalidDataException($"Unsupported prompt library version {document.Version}.");
            }

            if (document.History is null || document.Templates is null)
            {
                throw new InvalidDataException("The prompt library collections are missing.");
            }

            if (MaterializeDocument(document.Version, document.History, document.Templates, []))
            {
                // The file may change after the initial read and before this process can
                // acquire the cross-process writer lock. Reload and normalize the winner
                // under that lock rather than publishing this stale snapshot.
                _beforeNormalizationWriteLock?.Invoke();
                using FileStream writeLock = BeginWrite();
                if (ReloadForWrite())
                {
                    WriteDocument(_history, _templates, writeLock);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            RecoverFromCorruption();
        }
    }

    private static List<PromptHistoryEntry> LoadHistory(
        IReadOnlyCollection<StoredHistoryEntry> storedItems,
        HashSet<Guid> ids,
        bool migrateLegacyPrompts,
        out bool changed)
    {
        var items = new List<PromptHistoryEntry>(storedItems.Count);
        changed = false;

        foreach (StoredHistoryEntry stored in storedItems)
        {
            ValidateStoredId(stored.Id, ids);
            PromptTaskKind taskKind = ValidateStoredTaskKind(stored.TaskKind);
            string? prompt = NormalizeStoredPrompt(stored.Prompt, migrateLegacyPrompts);
            if (stored.LastUsedAtUtc is not { } lastUsedAtUtc || stored.UseCount < 1)
            {
                throw new InvalidDataException("A prompt history entry has invalid usage data.");
            }
            if (prompt is null)
            {
                changed = true;
                continue;
            }

            DateTimeOffset normalizedTimestamp = lastUsedAtUtc.ToUniversalTime();
            changed |= !string.Equals(stored.Prompt, prompt, StringComparison.Ordinal)
                || stored.LastUsedAtUtc != normalizedTimestamp;
            items.Add(new PromptHistoryEntry(
                stored.Id,
                taskKind,
                prompt,
                normalizedTimestamp,
                stored.UseCount,
                stored.IsPinned));
        }

        items.Sort((left, right) => right.LastUsedAtUtc.CompareTo(left.LastUsedAtUtc));
        var coalesced = new List<PromptHistoryEntry>(items.Count);
        var historyIndex = new Dictionary<(PromptTaskKind Kind, string Prompt), int>();
        foreach (PromptHistoryEntry item in items)
        {
            if (!historyIndex.TryGetValue((item.TaskKind, item.Prompt), out int index))
            {
                historyIndex.Add((item.TaskKind, item.Prompt), coalesced.Count);
                coalesced.Add(item);
                continue;
            }

            PromptHistoryEntry existing = coalesced[index];
            coalesced[index] = existing with
            {
                UseCount = AddSaturating(existing.UseCount, item.UseCount),
                IsPinned = existing.IsPinned || item.IsPinned,
            };
            changed = true;
        }

        return coalesced;
    }

    private static List<PromptTemplate> LoadTemplates(
        IReadOnlyCollection<StoredTemplate> storedItems,
        HashSet<Guid> ids,
        bool migrateLegacyPrompts,
        out bool changed)
    {
        var items = new List<PromptTemplate>(storedItems.Count);
        changed = false;

        foreach (StoredTemplate stored in storedItems)
        {
            ValidateStoredId(stored.Id, ids);
            PromptTaskKind taskKind = ValidateStoredTaskKind(stored.TaskKind);
            string name = NormalizeStoredTemplateName(stored.Name);
            string? prompt = NormalizeStoredPrompt(stored.Prompt, migrateLegacyPrompts);
            if (stored.CreatedAtUtc is not { } createdAtUtc
                || stored.UpdatedAtUtc is not { } updatedAtUtc
                || createdAtUtc > updatedAtUtc)
            {
                throw new InvalidDataException("A prompt template has invalid timestamps.");
            }
            if (prompt is null)
            {
                changed = true;
                continue;
            }

            DateTimeOffset normalizedCreatedAt = createdAtUtc.ToUniversalTime();
            DateTimeOffset normalizedUpdatedAt = updatedAtUtc.ToUniversalTime();
            changed |= !string.Equals(stored.Name, name, StringComparison.Ordinal)
                || !string.Equals(stored.Prompt, prompt, StringComparison.Ordinal)
                || stored.CreatedAtUtc != normalizedCreatedAt
                || stored.UpdatedAtUtc != normalizedUpdatedAt;
            items.Add(new PromptTemplate(
                stored.Id,
                name,
                taskKind,
                prompt,
                normalizedCreatedAt,
                normalizedUpdatedAt,
                stored.IsPinned));
        }

        items.Sort((left, right) => right.UpdatedAtUtc.CompareTo(left.UpdatedAtUtc));
        var coalesced = new List<PromptTemplate>(items.Count);
        var indexesByTask = new Dictionary<PromptTaskKind, Dictionary<string, int>>();
        foreach (PromptTemplate item in items)
        {
            if (!indexesByTask.TryGetValue(item.TaskKind, out Dictionary<string, int>? indexesByName))
            {
                indexesByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                indexesByTask.Add(item.TaskKind, indexesByName);
            }
            if (!indexesByName.TryGetValue(item.Name, out int index))
            {
                indexesByName.Add(item.Name, coalesced.Count);
                coalesced.Add(item);
                continue;
            }

            PromptTemplate existing = coalesced[index];
            coalesced[index] = existing with
            {
                CreatedAtUtc = existing.CreatedAtUtc < item.CreatedAtUtc
                    ? existing.CreatedAtUtc
                    : item.CreatedAtUtc,
                IsPinned = existing.IsPinned || item.IsPinned,
            };
            changed = true;
        }

        return coalesced;
    }

    private static void ValidateStoredId(Guid id, HashSet<Guid> ids)
    {
        if (id == Guid.Empty || !ids.Add(id))
        {
            throw new InvalidDataException("A prompt library item has an invalid or duplicate ID.");
        }
    }

    private static PromptTaskKind ValidateStoredTaskKind(PromptTaskKind? taskKind)
    {
        if (taskKind is not { } value || !Enum.IsDefined(typeof(PromptTaskKind), value))
        {
            throw new InvalidDataException("A prompt library item has an invalid task kind.");
        }

        return value;
    }

    private static string? NormalizeStoredPrompt(string? prompt, bool migrateLegacyPrompt)
    {
        try
        {
            string normalized = NormalizePromptText(prompt!, nameof(prompt));
            int maximumLength = migrateLegacyPrompt
                ? LegacyMaxPromptLength
                : MaxPromptLength;
            if (normalized.Length > maximumLength)
            {
                throw new ArgumentException(
                    $"The prompt cannot exceed {maximumLength} characters.",
                    nameof(prompt));
            }
            // Version 1 permitted larger prompts. Drop only an entry that the
            // paid API can no longer submit, rather than treating its siblings
            // as corrupt or silently changing the prompt's meaning.
            return migrateLegacyPrompt && normalized.Length > MaxPromptLength
                ? null
                : normalized;
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException("A stored prompt is invalid.", ex);
        }
    }

    private static string NormalizeStoredTemplateName(string? name)
    {
        try
        {
            return NormalizeTemplateName(name!);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException("A stored template name is invalid.", ex);
        }
    }

    private void TrimRecentHistory(List<PromptHistoryEntry> history)
    {
        int recentCount = 0;
        for (int i = 0; i < history.Count; i++)
        {
            if (history[i].IsPinned)
            {
                continue;
            }

            recentCount++;
            if (recentCount > _options.MaxRecentItems)
            {
                history.RemoveAt(i);
                i--;
            }
        }
    }

    private FileStream BeginWrite()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
        long deadline = Environment.TickCount64 + WriteLockTimeoutMilliseconds;
        while (true)
        {
            try
            {
                return new FileStream(
                    _lockPath,
                    new FileStreamOptions
                    {
                        Mode = FileMode.OpenOrCreate,
                        Access = FileAccess.ReadWrite,
                        Share = FileShare.None,
                        BufferSize = 1,
                        Options = FileOptions.WriteThrough,
                    });
            }
            catch (IOException) when (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(10);
            }
        }
    }

    // The process lock protects the read-modify-write window. Reload while it is held so a
    // second library instance does not commit a stale in-memory snapshot over a newer change.
    private bool ReloadForWrite()
    {
        PromptHistoryEntry[] localTransientHistory = _options.RetainRecentPromptText
            ? []
            : _history.Where(item => !item.IsPinned).ToArray();
        if (!File.Exists(StoragePath))
        {
            _history = localTransientHistory.ToList();
            _templates = [];
            return false;
        }

        using FileStream stream = File.OpenRead(StoragePath);
        StorageDocument document = ReadStorageDocument(stream);
        if (document.Version > CurrentStorageVersion)
        {
            throw new NotSupportedException(
                $"Prompt library version {document.Version} is newer than supported version {CurrentStorageVersion}.");
        }
        if (document.Version < 1
            || document.History is null
            || document.Templates is null)
        {
            throw new InvalidDataException("The prompt library document is invalid.");
        }

        return MaterializeDocument(document.Version, document.History, document.Templates, localTransientHistory);
    }

    private static StorageDocument ReadStorageDocument(Stream stream)
        => JsonSerializer.Deserialize<StorageDocument>(stream, s_jsonOptions)
            ?? throw new InvalidDataException("The prompt library document is empty.");

    // Publishes a validated document as the in-memory library, keeping this process's own
    // transient entries when recent prompts are not retained on disk. True when the file
    // needs rewriting: a legacy version, entries normalized on load, or history dropped by
    // retention or trimming.
    private bool MaterializeDocument(
        int version,
        IReadOnlyCollection<StoredHistoryEntry> storedHistory,
        IReadOnlyCollection<StoredTemplate> storedTemplates,
        PromptHistoryEntry[] localTransientHistory)
    {
        var ids = new HashSet<Guid>();
        bool migrateLegacyPrompts = version == 1;
        List<PromptHistoryEntry> history = LoadHistory(
            storedHistory,
            ids,
            migrateLegacyPrompts,
            out bool historyChanged);
        List<PromptTemplate> templates = LoadTemplates(
            storedTemplates,
            ids,
            migrateLegacyPrompts,
            out bool templatesChanged);
        bool retentionChanged = false;
        if (!_options.RetainRecentPromptText)
        {
            retentionChanged = history.RemoveAll(item => !item.IsPinned) > 0;
            var persistedIds = history.Select(item => item.Id).ToHashSet();
            history.InsertRange(
                0,
                localTransientHistory.Where(item => !persistedIds.Contains(item.Id)));
        }
        int historyCount = history.Count;
        TrimRecentHistory(history);
        bool trimChanged = history.Count != historyCount;
        _history = history;
        _templates = templates;
        return migrateLegacyPrompts
            || historyChanged
            || templatesChanged
            || retentionChanged
            || trimChanged;
    }

    private void Commit(
        List<PromptHistoryEntry> history,
        List<PromptTemplate> templates,
        FileStream writeLock)
    {
        WriteDocument(history, templates, writeLock);
        _history = history;
        _templates = templates;
    }

    private void WriteDocument(
        IReadOnlyCollection<PromptHistoryEntry> history,
        IReadOnlyCollection<PromptTemplate> templates,
        FileStream? writeLock = null)
    {
        using FileStream? ownedLock = writeLock is null ? BeginWrite() : null;
        var document = new StorageDocument
        {
            Version = CurrentStorageVersion,
            History = history
                .Where(item => _options.RetainRecentPromptText || item.IsPinned)
                .Select(StoredHistoryEntry.FromModel)
                .ToList(),
            Templates = templates.Select(StoredTemplate.FromModel).ToList(),
        };

        string directory = Path.GetDirectoryName(StoragePath)!;
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(StoragePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var streamOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 4096,
                Options = FileOptions.WriteThrough,
            };
            if (!OperatingSystem.IsWindows())
            {
                streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(tempPath, streamOptions))
            {
                JsonSerializer.Serialize(stream, document, s_jsonOptions);
                stream.Flush(true);
            }

            _replaceFile(tempPath, StoragePath);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static void ReplaceFile(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath, true);

    private void RecoverFromCorruption()
    {
        _beforeCorruptionWriteLock?.Invoke();
        using FileStream writeLock = BeginWrite();
        if (!File.Exists(StoragePath))
        {
            _history = [];
            _templates = [];
            WriteDocument(_history, _templates, writeLock);
            return;
        }

        try
        {
            if (ReloadForWrite())
            {
                WriteDocument(_history, _templates, writeLock);
            }
            return;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            // The current lock owner is responsible for quarantining the corrupt winner below.
        }

        string timestamp = _timeProvider.GetUtcNow()
            .ToUniversalTime()
            .ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        string recoveryPath;
        do
        {
            recoveryPath = $"{StoragePath}.corrupt-{timestamp}-{Guid.NewGuid():N}";
        }
        while (File.Exists(recoveryPath));

        File.Move(StoragePath, recoveryPath);
        RecoveredCorruptFilePath = recoveryPath;
        _history = [];
        _templates = [];
        WriteDocument(_history, _templates, writeLock);
    }

    private sealed class StorageDocument
    {
        public int Version { get; set; }

        public List<StoredHistoryEntry>? History { get; set; }

        public List<StoredTemplate>? Templates { get; set; }
    }

    private sealed class StoredHistoryEntry
    {
        public Guid Id { get; set; }

        public PromptTaskKind? TaskKind { get; set; }

        public string? Prompt { get; set; }

        public DateTimeOffset? LastUsedAtUtc { get; set; }

        public int UseCount { get; set; }

        public bool IsPinned { get; set; }

        public static StoredHistoryEntry FromModel(PromptHistoryEntry model) => new()
        {
            Id = model.Id,
            TaskKind = model.TaskKind,
            Prompt = model.Prompt,
            LastUsedAtUtc = model.LastUsedAtUtc,
            UseCount = model.UseCount,
            IsPinned = model.IsPinned,
        };
    }

    private sealed class StoredTemplate
    {
        public Guid Id { get; set; }

        public string? Name { get; set; }

        public PromptTaskKind? TaskKind { get; set; }

        public string? Prompt { get; set; }

        public DateTimeOffset? CreatedAtUtc { get; set; }

        public DateTimeOffset? UpdatedAtUtc { get; set; }

        public bool IsPinned { get; set; }

        public static StoredTemplate FromModel(PromptTemplate model) => new()
        {
            Id = model.Id,
            Name = model.Name,
            TaskKind = model.TaskKind,
            Prompt = model.Prompt,
            CreatedAtUtc = model.CreatedAtUtc,
            UpdatedAtUtc = model.UpdatedAtUtc,
            IsPinned = model.IsPinned,
        };
    }
}
