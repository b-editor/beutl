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
