using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

namespace BEditorNext;

public readonly struct ResourceReference<T> : IEquatable<ResourceReference<T>>
{
    private static IResourceProvider? _resourceProvider;

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

    public T FindOrDefault(T defaultValue)
    {
        _resourceProvider ??= ServiceLocator.Current.GetRequiredService<IResourceProvider>();

        if (_resourceProvider.TryFindResource(this, out T? value))
        {
            return value;
        }
        else
        {
            return defaultValue;
        }
    }

    public T? FindOrDefault()
    {
        _resourceProvider ??= ServiceLocator.Current.GetRequiredService<IResourceProvider>();

        if (_resourceProvider.TryFindResource(this, out T? value))
        {
            return value;
        }
        else
        {
            return default;
        }
    }

    public bool TryFindResource([NotNullWhen(true)] out T? value)
    {
        _resourceProvider ??= ServiceLocator.Current.GetRequiredService<IResourceProvider>();

        return _resourceProvider.TryFindResource(this, out value);
    }
    
    public IObservable<T?> GetResourceObservable()
    {
        _resourceProvider ??= ServiceLocator.Current.GetRequiredService<IResourceProvider>();

        return _resourceProvider.GetResourceObservable(this);
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
