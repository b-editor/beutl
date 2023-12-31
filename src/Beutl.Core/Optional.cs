namespace Beutl;

public readonly struct Optional<T>(T value) : IEquatable<Optional<T>>, IOptional
{
    public bool HasValue { get; } = true;

    public T Value => HasValue ? value : throw new InvalidOperationException("Optional has no value.");

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
        return HasValue ? value?.GetHashCode() ?? 0 : 0;
    }

    public Optional<object?> ToObject()
    {
        return HasValue ? new Optional<object?>(value) : default;
    }

    public override string ToString()
    {
        return HasValue ? value?.ToString() ?? "(null)" : "(empty)";
    }

    public T? GetValueOrDefault()
    {
        return HasValue ? value : default;
    }

    public T? GetValueOrDefault(T defaultValue)
    {
        return HasValue ? value : defaultValue;
    }

    public TResult? GetValueOrDefault<TResult>()
    {
        return HasValue ?
            value is TResult result ? result : default
            : default;
    }

    public TResult? GetValueOrDefault<TResult>(TResult defaultValue)
    {
        return HasValue ?
            value is TResult result ? result : default
            : defaultValue;
    }

    Type IOptional.GetValueType() => typeof(T);

    public static implicit operator Optional<T>(T value) => new(value);

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
