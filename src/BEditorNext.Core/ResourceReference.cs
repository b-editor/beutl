namespace BEditorNext;

public readonly struct ResourceReference<T> : IEquatable<ResourceReference<T>>
{
    public ResourceReference(string key)
    {
        Key = key;
    }

    public string Key { get; }

    public override bool Equals(object? obj)
    {
        return obj is ResourceReference<T> reference && Equals(reference);
    }

    public bool Equals(ResourceReference<T> other)
    {
        return Key == other.Key;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Key);
    }

    public static bool operator ==(ResourceReference<T> left, ResourceReference<T> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ResourceReference<T> left, ResourceReference<T> right)
    {
        return !(left == right);
    }

    public static implicit operator ResourceReference<T>(string key)
    {
        return new ResourceReference<T>(key);
    }
}
