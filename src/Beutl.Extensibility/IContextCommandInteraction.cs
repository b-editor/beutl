namespace Beutl.Extensibility;

/// <summary>Requests successive input or selection steps while a palette command executes.</summary>
public interface IContextCommandInteraction
{
    CancellationToken CancellationToken { get; }

    /// <summary>Returns the entered text, or null when the request is cancelled.</summary>
    Task<string?> ShowInputAsync(
        ContextCommandInputOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the selected item, or null when the request is cancelled.</summary>
    Task<ContextCommandPickItem<T>?> ShowQuickPickAsync<T>(
        IReadOnlyList<ContextCommandPickItem<T>> items,
        ContextCommandPickOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed class ContextCommandInputOptions
{
    public string? Title { get; init; }

    public string? Prompt { get; init; }

    public string? Placeholder { get; init; }

    public string Value { get; init; } = string.Empty;

    /// <summary>Returns an error to prevent confirmation, or null when the value is valid.</summary>
    public Func<string, string?>? Validate { get; init; }
}

public sealed class ContextCommandPickOptions
{
    public string? Title { get; init; }

    public string? Prompt { get; init; }

    public string? Placeholder { get; init; }
}

public sealed record ContextCommandPickItem<T>(string Label, T Value, string? Description = null);
