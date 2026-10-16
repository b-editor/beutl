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

            if (document.Version != CurrentStorageVersion)
            {
                throw new InvalidDataException($"Unsupported prompt library version {document.Version}.");
            }

            if (document.History is null || document.Templates is null)
            {
                throw new InvalidDataException("The prompt library collections are missing.");
            }

            // Only held in memory: the next change writes the normalized entries.
            MaterializeDocument(document.History, document.Templates, []);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            RecoverFromCorruption();
        }
    }

    private static List<PromptHistoryEntry> LoadHistory(
        IReadOnlyCollection<StoredHistoryEntry> storedItems,
        HashSet<Guid> ids)
    {
        var items = new List<PromptHistoryEntry>(storedItems.Count);

        foreach (StoredHistoryEntry stored in storedItems)
        {
            ValidateStoredId(stored.Id, ids);
            PromptTaskKind taskKind = ValidateStoredTaskKind(stored.TaskKind);
            string prompt = NormalizeStoredPrompt(stored.Prompt);
            if (stored.LastUsedAtUtc is not { } lastUsedAtUtc || stored.UseCount < 1)
            {
                throw new InvalidDataException("A prompt history entry has invalid usage data.");
            }

            items.Add(new PromptHistoryEntry(
                stored.Id,
                taskKind,
                prompt,
                lastUsedAtUtc.ToUniversalTime(),
                stored.UseCount,
                stored.IsPinned));
        }

        items.Sort((left, right) => right.LastUsedAtUtc.CompareTo(left.LastUsedAtUtc));
        return items;
    }

    private static List<PromptTemplate> LoadTemplates(
        IReadOnlyCollection<StoredTemplate> storedItems,
        HashSet<Guid> ids)
    {
        var items = new List<PromptTemplate>(storedItems.Count);

        foreach (StoredTemplate stored in storedItems)
        {
            ValidateStoredId(stored.Id, ids);
            PromptTaskKind taskKind = ValidateStoredTaskKind(stored.TaskKind);
            string name = NormalizeStoredTemplateName(stored.Name);
            string prompt = NormalizeStoredPrompt(stored.Prompt);
            if (stored.CreatedAtUtc is not { } createdAtUtc
                || stored.UpdatedAtUtc is not { } updatedAtUtc
                || createdAtUtc > updatedAtUtc)
            {
                throw new InvalidDataException("A prompt template has invalid timestamps.");
            }

            items.Add(new PromptTemplate(
                stored.Id,
                name,
                taskKind,
                prompt,
                createdAtUtc.ToUniversalTime(),
                updatedAtUtc.ToUniversalTime(),
                stored.IsPinned));
        }

        items.Sort((left, right) => right.UpdatedAtUtc.CompareTo(left.UpdatedAtUtc));
        return items;
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

    private static string NormalizeStoredPrompt(string? prompt)
    {
        try
        {
            return NormalizePrompt(prompt!, nameof(prompt));
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
    private void ReloadForWrite()
    {
        PromptHistoryEntry[] localTransientHistory = _history.Where(item => !item.IsPinned).ToArray();
        if (!File.Exists(StoragePath))
        {
            _history = localTransientHistory.ToList();
            _templates = [];
            return;
        }

        using FileStream stream = File.OpenRead(StoragePath);
        StorageDocument document = ReadStorageDocument(stream);
        if (document.Version > CurrentStorageVersion)
        {
            throw new NotSupportedException(
                $"Prompt library version {document.Version} is newer than supported version {CurrentStorageVersion}.");
        }
        if (document.Version != CurrentStorageVersion
            || document.History is null
            || document.Templates is null)
        {
            throw new InvalidDataException("The prompt library document is invalid.");
        }

        MaterializeDocument(document.History, document.Templates, localTransientHistory);
    }

    private static StorageDocument ReadStorageDocument(Stream stream)
        => JsonSerializer.Deserialize<StorageDocument>(stream, s_jsonOptions)
            ?? throw new InvalidDataException("The prompt library document is empty.");

    // Publishes a validated document as the in-memory library, keeping this process's own
    // transient entries, as unpinned prompts are never written to disk.
    private void MaterializeDocument(
        IReadOnlyCollection<StoredHistoryEntry> storedHistory,
        IReadOnlyCollection<StoredTemplate> storedTemplates,
        PromptHistoryEntry[] localTransientHistory)
    {
        var ids = new HashSet<Guid>();
        List<PromptHistoryEntry> history = LoadHistory(storedHistory, ids);
        List<PromptTemplate> templates = LoadTemplates(storedTemplates, ids);
        history.RemoveAll(item => !item.IsPinned);
        var persistedIds = history.Select(item => item.Id).ToHashSet();
        history.InsertRange(
            0,
            localTransientHistory.Where(item => !persistedIds.Contains(item.Id)));
        TrimRecentHistory(history);
        _history = history;
        _templates = templates;
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
                .Where(item => item.IsPinned)
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
            ReloadForWrite();
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
