namespace Beutl;

public readonly struct Optional<T> : IEquatable<Optional<T>>
{
    private readonly T _value;

    public Optional(T value)
    {
        _value = value;
        HasValue = true;
    }

    public bool HasValue { get; }

    public T Value => HasValue ? _value : throw new InvalidOperationException("Optional has no value.");

    public override bool Equals(object? obj)
    {
        return obj is Optional<T> o && this == o;
    }

    public bool Equals(Optional<T> other)
    {
        return this == other;
    }

    public override int GetHashCode()
    {
        return HasValue ? _value?.GetHashCode() ?? 0 : 0;
    }

    public Optional<object?> ToObject()
    {
        return HasValue ? new Optional<object?>(_value) : default;
    }

    public override string ToString()
    {
        return HasValue ? _value?.ToString() ?? "(null)" : "(empty)";
    }

    public T? GetValueOrDefault()
    {
        return HasValue ? _value : default;
    }

    public T? GetValueOrDefault(T defaultValue)
    {
        return HasValue ? _value : defaultValue;
    }

    public TResult? GetValueOrDefault<TResult>()
    {
        return HasValue ?
            _value is TResult result ? result : default
            : default;
    }

    public TResult? GetValueOrDefault<TResult>(TResult defaultValue)
    {
        return HasValue ?
            _value is TResult result ? result : default
            : defaultValue;
    }

    public static implicit operator Optional<T>(T value) => new Optional<T>(value);

    public static bool operator !=(Optional<T> x, Optional<T> y) => !(x == y);

    public static bool operator ==(Optional<T> x, Optional<T> y)
    {
        if (!x.HasValue && !y.HasValue)
        {
            return true;
        }
        else if (x.HasValue && y.HasValue)
        {
            return EqualityComparer<T>.Default.Equals(x.Value, y.Value);
        }
        else
        {
            return false;
        }
    }

    public static Optional<T> Empty => default;
}
