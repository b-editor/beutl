namespace Beutl.Extensibility;

[AttributeUsage(AttributeTargets.Method)]
public class ContextCommandAttribute : Attribute
{
    public string? Name { get; set; }
}

public class ContextCommandDefinition(
    string name,
    string? displayName = null,
    string? description = null,
    ContextCommandKeyGesture[]? keyGestures = null)
{
    public string Name { get; init; } = name;

    public string? DisplayName { get; init; } = displayName;

    public string? Description { get; init; } = description;

    public ContextCommandKeyGesture[]? KeyGestures { get; init; } = keyGestures;
}

public class ContextCommandKeyGesture(string? keyGesture, PlatformID? platformId)
{
    public string? KeyGesture { get; init; } = keyGesture;

    public PlatformID? PlatformId { get; init; } = platformId;
}
