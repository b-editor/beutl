namespace Beutl.Services.Tutorials;

public sealed class TargetElementDefinition
{
    public string? ElementName { get; init; }

    public Func<object?>? ElementResolver { get; init; }

    public Type? ToolTabType { get; init; }

    public bool IsPrimary { get; init; } = false;
}
