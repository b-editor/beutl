namespace Beutl.Services.Tutorials;

public sealed class TutorialStep
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Content { get; init; }

    public IReadOnlyList<TargetElementDefinition>? TargetElements { get; init; }

    public TutorialStepPlacement PreferredPlacement { get; init; } = TutorialStepPlacement.Center;

    public bool IsActionRequired { get; init; }

    public Func<bool>? CanAdvance { get; init; }

    public Action? OnShown { get; init; }

    public Action? OnDismissed { get; init; }
}
