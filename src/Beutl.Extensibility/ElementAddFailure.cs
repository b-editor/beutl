namespace Beutl.Extensibility;

public readonly struct ElementAddFailureId : IEquatable<ElementAddFailureId>
{
    private readonly string? _value;

    public ElementAddFailureId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value.Trim();
    }

    public string Value => _value ?? string.Empty;

    public bool Equals(ElementAddFailureId other)
        => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj)
        => obj is ElementAddFailureId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(ElementAddFailureId left, ElementAddFailureId right)
        => left.Equals(right);

    public static bool operator !=(ElementAddFailureId left, ElementAddFailureId right)
        => !left.Equals(right);
}

/// <summary>
/// Base type for failures returned by the element-add pipeline. Source handlers may expose
/// their own typed failures with an open identifier.
/// </summary>
public abstract record ElementAddFailure
{
    protected ElementAddFailure(
        ElementAddFailureId id,
        string message,
        Exception? exception = null)
    {
        if (id.Value.Length == 0)
            throw new ArgumentException("A failure identifier is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Id = id;
        Message = message;
        Exception = exception;
    }

    public ElementAddFailureId Id { get; }

    public string Message { get; }

    public Exception? Exception { get; }
}
