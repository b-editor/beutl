using System.Collections;
using System.Collections.ObjectModel;

namespace Beutl.Editor.Services.Captions;

/// <summary>
/// An immutable, structurally comparable collection of extensible caption metadata.
/// </summary>
public sealed class CaptionMetadata : IReadOnlyDictionary<string, string>, IEquatable<CaptionMetadata>
{
    private readonly ReadOnlyDictionary<string, string> _values;

    public CaptionMetadata(IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string value) in values)
        {
            ValidateEntry(key, value);
            if (!copy.TryAdd(key, value))
                throw new ArgumentException($"Metadata key '{key}' occurs more than once.", nameof(values));
        }

        _values = new ReadOnlyDictionary<string, string>(copy);
    }

    private CaptionMetadata(Dictionary<string, string> values)
    {
        _values = new ReadOnlyDictionary<string, string>(values);
    }

    public static CaptionMetadata Empty { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

    public int Count => _values.Count;

    public IEnumerable<string> Keys => _values.Keys;

    public IEnumerable<string> Values => _values.Values;

    public string this[string key] => _values[key];

    public CaptionMetadata Set(string key, string value)
    {
        ValidateEntry(key, value);
        if (_values.TryGetValue(key, out string? existing)
            && string.Equals(existing, value, StringComparison.Ordinal))
        {
            return this;
        }

        var copy = new Dictionary<string, string>(_values, StringComparer.Ordinal)
        {
            [key] = value,
        };
        return new CaptionMetadata(copy);
    }

    public CaptionMetadata Remove(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!_values.ContainsKey(key))
            return this;

        var copy = new Dictionary<string, string>(_values, StringComparer.Ordinal);
        copy.Remove(key);
        return copy.Count == 0 ? Empty : new CaptionMetadata(copy);
    }

    public string? GetValueOrDefault(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _values.GetValueOrDefault(key);
    }

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    public bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(CaptionMetadata? other)
    {
        if (ReferenceEquals(this, other))
            return true;

        if (other is null || Count != other.Count)
            return false;

        foreach ((string key, string value) in _values)
        {
            if (!other._values.TryGetValue(key, out string? otherValue)
                || !string.Equals(value, otherValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as CaptionMetadata);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach ((string key, string value) in _values.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    internal CaptionMetadata RetainMatching(CaptionMetadata other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var matches = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string value) in _values)
        {
            if (other._values.TryGetValue(key, out string? otherValue)
                && string.Equals(value, otherValue, StringComparison.Ordinal))
            {
                matches.Add(key, value);
            }
        }

        return matches.Count == 0 ? Empty : new CaptionMetadata(matches);
    }

    private static void ValidateEntry(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
    }
}

/// <summary>
/// Metadata keys interpreted by the built-in caption codecs.
/// Third-party codecs may define additional namespaced keys.
/// </summary>
public static class CaptionMetadataKeys
{
    public const string WebVttClasses = "webvtt.classes";

    public const string AssStyle = "ass.style";
}
