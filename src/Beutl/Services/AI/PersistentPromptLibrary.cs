namespace Beutl.Services.AI;

internal sealed partial class PersistentPromptLibrary : IPromptLibrary, IPromptLibraryChangeSource
{
    internal const int CurrentStorageVersion = 2;
    private const int LegacyMaxPromptLength = 32_768;
    // Templates model prompts that can be sent to the paid API. Keeping oversized text here
    // would create reusable entries that can never pass final request validation.
    internal const int MaxPromptLength = Beutl.Api.Services.AiRequestLimits.MaxPromptLength;
    internal const int MaxTemplateNameLength = 128;
    private const int WriteLockTimeoutMilliseconds = 2_000;

    private readonly object _gate = new();
    private readonly PromptLibraryOptions _options;
    private readonly Action<string, string> _replaceFile;
    private readonly Action? _beforeNormalizationWriteLock;
    private readonly Action? _beforeCorruptionWriteLock;
    private readonly TimeProvider _timeProvider;
    private readonly string _lockPath;
    private List<PromptHistoryEntry> _history = [];
    private List<PromptTemplate> _templates = [];

    public PersistentPromptLibrary(
        string storagePath,
        PromptLibraryOptions? options = null,
        TimeProvider? timeProvider = null,
        Action<string, string>? replaceFile = null,
        Action? beforeNormalizationWriteLock = null,
        Action? beforeCorruptionWriteLock = null)
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
        _lockPath = StoragePath + ".lock";
        _replaceFile = replaceFile ?? ReplaceFile;
        _beforeNormalizationWriteLock = beforeNormalizationWriteLock;
        _beforeCorruptionWriteLock = beforeCorruptionWriteLock;

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

    public IDisposable SubscribeChanged(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return PromptLibraryChangeHub.Subscribe(path =>
        {
            if (StringComparer.Ordinal.Equals(StoragePath, path))
                callback();
        });
    }

    public PromptHistoryEntry Record(PromptTaskKind taskKind, string prompt)
    {
        ValidateTaskKind(taskKind);
        string normalizedPrompt = NormalizePrompt(prompt, nameof(prompt));

        PromptHistoryEntry entry;
        lock (_gate)
        {
            using FileStream writeLock = BeginWrite();
            ReloadForWrite();
            List<PromptHistoryEntry> history = [.. _history];
            DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();
            int index = history.FindIndex(item =>
                item.TaskKind == taskKind
                && string.Equals(item.Prompt, normalizedPrompt, StringComparison.Ordinal));

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
            Commit(history, [.. _templates], writeLock);
        }
        PublishChanged();
        return entry;
    }

    public PromptTemplate SaveTemplate(string name, PromptTaskKind taskKind, string prompt)
    {
        ValidateTaskKind(taskKind);
        string normalizedName = NormalizeTemplateName(name);
        string normalizedPrompt = NormalizePrompt(prompt, nameof(prompt));

        PromptTemplate template;
        lock (_gate)
        {
            using FileStream writeLock = BeginWrite();
            ReloadForWrite();
            List<PromptTemplate> templates = [.. _templates];
            DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();
            int index = templates.FindIndex(item =>
                item.TaskKind == taskKind
                && string.Equals(item.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

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
            Commit([.. _history], templates, writeLock);
        }
        PublishChanged();
        return template;
    }

    public bool SetHistoryPinned(Guid id, bool isPinned)
    {
        lock (_gate)
        {
            using FileStream writeLock = BeginWrite();
            ReloadForWrite();
            List<PromptHistoryEntry> history = [.. _history];
            int index = history.FindIndex(item => item.Id == id);
            if (index < 0 || history[index].IsPinned == isPinned)
            {
                return false;
            }

            history[index] = history[index] with { IsPinned = isPinned };
            TrimRecentHistory(history);
            Commit(history, [.. _templates], writeLock);
        }
        PublishChanged();
        return true;
    }

    public bool SetTemplatePinned(Guid id, bool isPinned)
    {
        lock (_gate)
        {
            using FileStream writeLock = BeginWrite();
            ReloadForWrite();
            List<PromptTemplate> templates = [.. _templates];
            int index = templates.FindIndex(item => item.Id == id);
            if (index < 0 || templates[index].IsPinned == isPinned)
            {
                return false;
            }

            templates[index] = templates[index] with { IsPinned = isPinned };
            Commit([.. _history], templates, writeLock);
        }
        PublishChanged();
        return true;
    }

    public bool DeleteHistory(Guid id)
    {
        lock (_gate)
        {
            using FileStream writeLock = BeginWrite();
            ReloadForWrite();
            List<PromptHistoryEntry> history = [.. _history];
            int index = history.FindIndex(item => item.Id == id);
            if (index < 0)
            {
                return false;
            }

            history.RemoveAt(index);
            Commit(history, [.. _templates], writeLock);
        }
        PublishChanged();
        return true;
    }

    public bool DeleteTemplate(Guid id)
    {
        lock (_gate)
        {
            using FileStream writeLock = BeginWrite();
            ReloadForWrite();
            List<PromptTemplate> templates = [.. _templates];
            int index = templates.FindIndex(item => item.Id == id);
            if (index < 0)
            {
                return false;
            }

            templates.RemoveAt(index);
            Commit([.. _history], templates, writeLock);
        }
        PublishChanged();
        return true;
    }

    public void ClearHistory()
    {
        bool changed = false;
        lock (_gate)
        {
            using FileStream writeLock = BeginWrite();
            ReloadForWrite();
            if (_history.Count > 0)
            {
                Commit([], [.. _templates], writeLock);
                changed = true;
            }
        }
        if (changed)
            PublishChanged();
    }

    public void ClearTemplates()
    {
        bool changed = false;
        lock (_gate)
        {
            using FileStream writeLock = BeginWrite();
            ReloadForWrite();
            if (_templates.Count > 0)
            {
                Commit([.. _history], [], writeLock);
                changed = true;
            }
        }
        if (changed)
            PublishChanged();
    }

    public void ClearAll()
    {
        bool changed = false;
        lock (_gate)
        {
            using FileStream writeLock = BeginWrite();
            ReloadForWrite();
            if (_history.Count > 0 || _templates.Count > 0)
            {
                Commit([], [], writeLock);
                changed = true;
            }
        }
        if (changed)
            PublishChanged();
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
        string normalized = NormalizePromptText(prompt, parameterName);

        if (normalized.Length > MaxPromptLength)
        {
            throw new ArgumentException(
                $"The prompt cannot exceed {MaxPromptLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static string NormalizePromptText(string prompt, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(prompt, parameterName);
        string normalized = prompt
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("A prompt is required.", parameterName);

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

    private void PublishChanged()
        => PromptLibraryChangeHub.Publish(StoragePath);
}
