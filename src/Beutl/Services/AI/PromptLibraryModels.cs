namespace Beutl.Services.AI;

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

internal sealed record PromptLibraryOptions
{
    public const int DefaultMaxRecentItems = 50;
    public const int MaximumMaxRecentItems = 500;

    public int MaxRecentItems { get; init; } = DefaultMaxRecentItems;

    // Named templates and pinned history are explicit saves and remain persistent.
    public bool RetainRecentPromptText { get; init; }
}
