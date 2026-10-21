namespace Beutl.Editor.Components.AiWorkspaceTab.Services;

internal enum PromptTaskKind
{
    Image,
    ImageEdit,
    Video,
}

internal sealed record PromptHistoryEntry(
    Guid Id,
    PromptTaskKind TaskKind,
    string Prompt,
    DateTimeOffset LastUsedAtUtc,
    int UseCount,
    bool IsPinned);

internal sealed record PromptTemplate(
    Guid Id,
    string Name,
    PromptTaskKind TaskKind,
    string Prompt,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool IsPinned);

// Tests shorten the recent history; the app always uses the default.
internal sealed record PromptLibraryOptions
{
    public const int DefaultMaxRecentItems = 50;

    public int MaxRecentItems { get; init; } = DefaultMaxRecentItems;
}
