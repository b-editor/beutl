namespace Beutl.Services.AI;

internal interface IPromptLibrary
{
    string StoragePath { get; }

    bool RetainRecentPromptText { get; }

    string? RecoveredCorruptFilePath { get; }

    IReadOnlyList<PromptHistoryEntry> History { get; }

    IReadOnlyList<PromptTemplate> Templates { get; }

    PromptHistoryEntry Record(PromptTaskKind taskKind, string prompt);

    PromptTemplate SaveTemplate(string name, PromptTaskKind taskKind, string prompt);

    bool SetHistoryPinned(Guid id, bool isPinned);

    bool SetTemplatePinned(Guid id, bool isPinned);

    bool DeleteHistory(Guid id);

    bool DeleteTemplate(Guid id);

    void ClearHistory();

    void ClearTemplates();

    void ClearAll();
}

/// <summary>
/// Publishes committed prompt-library changes without making observation part of the storage
/// contract required by simple or read-only implementations.
/// </summary>
internal interface IPromptLibraryChangeSource
{
    IDisposable SubscribeChanged(Action callback);
}

internal static class PromptLibraryChangeHub
{
    private static event Action<string>? Changed;

    public static IDisposable Subscribe(Action<string> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Changed += callback;
        return new Subscription(callback);
    }

    public static void Publish(string storagePath)
    {
        foreach (Action<string> callback in Changed?.GetInvocationList().Cast<Action<string>>()
            ?? Array.Empty<Action<string>>())
        {
            try
            {
                callback(storagePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    "A prompt-library change observer failed: {0}",
                    ex.Message);
            }
        }
    }

    private sealed class Subscription(Action<string> callback) : IDisposable
    {
        private Action<string>? _callback = callback;

        public void Dispose()
        {
            Action<string>? callback = Interlocked.Exchange(ref _callback, null);
            if (callback is not null)
                Changed -= callback;
        }
    }
}
