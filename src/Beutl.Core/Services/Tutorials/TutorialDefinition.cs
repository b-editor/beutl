namespace Beutl.Services.Tutorials;

public sealed class TutorialDefinition
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyList<TutorialStep> Steps { get; init; }

    public Func<Task<bool>>? CanStart { get; init; }

    public Func<Task<bool>>? FulfillPrerequisites { get; init; }

    public string? Category { get; init; }

    public int Priority { get; init; }
}
