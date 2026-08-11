using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.Services.AI;

internal sealed class PersistentPromptLibrary : IPromptLibrary
{
    internal const int CurrentStorageVersion = 1;
    internal const int MaxPromptLength = 32_768;
    internal const int MaxTemplateNameLength = 128;

    private static readonly JsonSerializerOptions s_jsonOptions = CreateJsonOptions();

    private readonly object _gate = new();
    private readonly PromptLibraryOptions _options;
    private readonly Action<string, string> _replaceFile;
    private readonly TimeProvider _timeProvider;
    private List<PromptHistoryEntry> _history = [];
    private List<PromptTemplate> _templates = [];

    public PersistentPromptLibrary(
        string storagePath,
        PromptLibraryOptions? options = null,
        TimeProvider? timeProvider = null,
        Action<string, string>? replaceFile = null)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            throw new ArgumentException("A storage file path is required.", nameof(storagePath));
        }

        StoragePath = Path.GetFullPath(storagePath);
        if (string.IsNullOrEmpty(Path.GetFileName(StoragePath)) || Directory.Exists(StoragePath))
        {
            throw new ArgumentException("The storage path must identify a file.", nameof(storagePath));
        }

        _options = options ?? new PromptLibraryOptions();
        ValidateOptions(_options);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _replaceFile = replaceFile ?? ReplaceFile;

        if (File.Exists(StoragePath))
        {
            Load();
        }
    }

    public string StoragePath { get; }

    public bool RetainRecentPromptText => _options.RetainRecentPromptText;

    public string? RecoveredCorruptFilePath { get; private set; }

    public IReadOnlyList<PromptHistoryEntry> History
    {
        get
        {
            lock (_gate)
            {
                return _history.ToArray();
            }
        }
    }

    public IReadOnlyList<PromptTemplate> Templates
    {
        get
        {
            lock (_gate)
            {
                return _templates.ToArray();
            }
        }
    }

    public PromptHistoryEntry Record(PromptTaskKind taskKind, string prompt)
    {
        ValidateTaskKind(taskKind);
        string normalizedPrompt = NormalizePrompt(prompt, nameof(prompt));

        lock (_gate)
        {
            List<PromptHistoryEntry> history = [.. _history];
            DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();
            int index = history.FindIndex(item =>
                item.TaskKind == taskKind
                && string.Equals(item.Prompt, normalizedPrompt, StringComparison.Ordinal));

            PromptHistoryEntry entry;
            if (index >= 0)
            {
                PromptHistoryEntry existing = history[index];
                entry = existing with
                {
                    LastUsedAtUtc = now,
                    UseCount = IncrementSaturating(existing.UseCount),
                };
                history.RemoveAt(index);
            }
            else
            {
                entry = new PromptHistoryEntry(
                    Guid.NewGuid(),
                    taskKind,
                    normalizedPrompt,
                    now,
                    1,
                    false);
            }

            history.Insert(0, entry);
            TrimRecentHistory(history);
            Commit(history, [.. _templates]);
            return entry;
        }
    }

    public PromptTemplate SaveTemplate(string name, PromptTaskKind taskKind, string prompt)
    {
        ValidateTaskKind(taskKind);
        string normalizedName = NormalizeTemplateName(name);
        string normalizedPrompt = NormalizePrompt(prompt, nameof(prompt));

        lock (_gate)
        {
            List<PromptTemplate> templates = [.. _templates];
            DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();
            int index = templates.FindIndex(item =>
                item.TaskKind == taskKind
                && string.Equals(item.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

            PromptTemplate template;
            if (index >= 0)
            {
                PromptTemplate existing = templates[index];
                template = existing with
                {
                    Name = normalizedName,
                    Prompt = normalizedPrompt,
                    UpdatedAtUtc = now,
                };
                templates.RemoveAt(index);
            }
            else
            {
                template = new PromptTemplate(
                    Guid.NewGuid(),
                    normalizedName,
                    taskKind,
                    normalizedPrompt,
                    now,
                    now,
                    false);
            }

            templates.Insert(0, template);
            Commit([.. _history], templates);
            return template;
        }
    }

    public bool SetHistoryPinned(Guid id, bool isPinned)
    {
        lock (_gate)
        {
            List<PromptHistoryEntry> history = [.. _history];
            int index = history.FindIndex(item => item.Id == id);
            if (index < 0 || history[index].IsPinned == isPinned)
            {
                return false;
            }

            history[index] = history[index] with { IsPinned = isPinned };
            TrimRecentHistory(history);
            Commit(history, [.. _templates]);
            return true;
        }
    }

    public bool SetTemplatePinned(Guid id, bool isPinned)
    {
        lock (_gate)
        {
            List<PromptTemplate> templates = [.. _templates];
            int index = templates.FindIndex(item => item.Id == id);
            if (index < 0 || templates[index].IsPinned == isPinned)
            {
                return false;
            }

            templates[index] = templates[index] with { IsPinned = isPinned };
            Commit([.. _history], templates);
            return true;
        }
    }

    public bool DeleteHistory(Guid id)
    {
        lock (_gate)
        {
            List<PromptHistoryEntry> history = [.. _history];
            int index = history.FindIndex(item => item.Id == id);
            if (index < 0)
            {
                return false;
            }

            history.RemoveAt(index);
            Commit(history, [.. _templates]);
            return true;
        }
    }

    public bool DeleteTemplate(Guid id)
    {
        lock (_gate)
        {
            List<PromptTemplate> templates = [.. _templates];
            int index = templates.FindIndex(item => item.Id == id);
            if (index < 0)
            {
                return false;
            }

            templates.RemoveAt(index);
            Commit([.. _history], templates);
            return true;
        }
    }

    public void ClearHistory()
    {
        lock (_gate)
        {
            if (_history.Count > 0)
            {
                Commit([], [.. _templates]);
            }
        }
    }

    public void ClearTemplates()
    {
        lock (_gate)
        {
            if (_templates.Count > 0)
            {
                Commit([.. _history], []);
            }
        }
    }

    public void ClearAll()
    {
        lock (_gate)
        {
            if (_history.Count > 0 || _templates.Count > 0)
            {
                Commit([], []);
            }
        }
    }

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

    private static void ValidateOptions(PromptLibraryOptions options)
    {
        if (options.MaxRecentItems is < 1 or > PromptLibraryOptions.MaximumMaxRecentItems)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxRecentItems,
                $"MaxRecentItems must be between 1 and {PromptLibraryOptions.MaximumMaxRecentItems}.");
        }
    }

    private static void ValidateTaskKind(PromptTaskKind taskKind)
    {
        if (!Enum.IsDefined(typeof(PromptTaskKind), taskKind))
        {
            throw new ArgumentOutOfRangeException(nameof(taskKind));
        }
    }

    private static string NormalizePrompt(string prompt, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(prompt, parameterName);
        string normalized = prompt
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("A prompt is required.", parameterName);
        }

        if (normalized.Length > MaxPromptLength)
        {
            throw new ArgumentException(
                $"The prompt cannot exceed {MaxPromptLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static string NormalizeTemplateName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string normalized = name.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("A template name is required.", nameof(name));
        }

        if (normalized.Length > MaxTemplateNameLength)
        {
            throw new ArgumentException(
                $"The template name cannot exceed {MaxTemplateNameLength} characters.",
                nameof(name));
        }

        if (normalized.Any(char.IsControl))
        {
            throw new ArgumentException("The template name cannot contain control characters.", nameof(name));
        }

        return normalized;
    }

    private static int IncrementSaturating(int value) => value == int.MaxValue ? value : value + 1;

    private static int AddSaturating(int left, int right) =>
        left > int.MaxValue - right ? int.MaxValue : left + right;

    private void Load()
    {
        try
        {
            using FileStream stream = File.OpenRead(StoragePath);
            StorageDocument document = JsonSerializer.Deserialize<StorageDocument>(stream, s_jsonOptions)
                ?? throw new InvalidDataException("The prompt library document is empty.");

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

            var ids = new HashSet<Guid>();
            List<PromptHistoryEntry> history = LoadHistory(document.History, ids, out bool historyChanged);
            List<PromptTemplate> templates = LoadTemplates(document.Templates, ids, out bool templatesChanged);

            bool retentionChanged = false;
            if (!_options.RetainRecentPromptText)
            {
                retentionChanged = history.RemoveAll(item => !item.IsPinned) > 0;
            }

            int historyCount = history.Count;
            TrimRecentHistory(history);
            bool trimChanged = history.Count != historyCount;

            _history = history;
            _templates = templates;

            if (historyChanged || templatesChanged || retentionChanged || trimChanged)
            {
                WriteDocument(_history, _templates);
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
        out bool changed)
    {
        var items = new List<PromptHistoryEntry>(storedItems.Count);
        changed = false;

        foreach (StoredHistoryEntry stored in storedItems)
        {
            ValidateStoredId(stored.Id, ids);
            PromptTaskKind taskKind = ValidateStoredTaskKind(stored.TaskKind);
            string prompt = NormalizeStoredPrompt(stored.Prompt);
            if (stored.LastUsedAtUtc is not { } lastUsedAtUtc || stored.UseCount < 1)
            {
                throw new InvalidDataException("A prompt history entry has invalid usage data.");
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
        foreach (PromptHistoryEntry item in items)
        {
            int index = coalesced.FindIndex(existing =>
                existing.TaskKind == item.TaskKind
                && string.Equals(existing.Prompt, item.Prompt, StringComparison.Ordinal));
            if (index < 0)
            {
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
        out bool changed)
    {
        var items = new List<PromptTemplate>(storedItems.Count);
        changed = false;

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
        foreach (PromptTemplate item in items)
        {
            int index = coalesced.FindIndex(existing =>
                existing.TaskKind == item.TaskKind
                && string.Equals(existing.Name, item.Name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
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

    private void Commit(List<PromptHistoryEntry> history, List<PromptTemplate> templates)
    {
        WriteDocument(history, templates);
        _history = history;
        _templates = templates;
    }

    private void WriteDocument(
        IReadOnlyCollection<PromptHistoryEntry> history,
        IReadOnlyCollection<PromptTemplate> templates)
    {
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
        WriteDocument(_history, _templates);
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
