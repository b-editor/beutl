namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

// Per-tab cache: immutable revisions can be revisited without spawning Git or parsing the same
// preview again. Metadata notifications invalidate it; expiry also bounds unobserved config changes.
internal sealed class VersionControlPreviewCache<TKey, TValue>(
    int maxEntries, long maxBytes, TimeProvider timeProvider) where TKey : notnull
{
    private sealed record Entry(TKey Key, TValue Value, long Bytes, long Created);
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _recent = new();
    private long _bytes;

    public bool TryGet(TKey key, out TValue value)
    {
        if (_entries.TryGetValue(key, out var node))
        {
            if (timeProvider.GetElapsedTime(node.Value.Created) < TimeSpan.FromSeconds(30))
            {
                _recent.Remove(node);
                _recent.AddLast(node);
                value = node.Value.Value;
                return true;
            }

            Remove(node);
        }

        value = default!;
        return false;
    }

    public void Add(TKey key, TValue value, long bytes)
    {
        if (_entries.TryGetValue(key, out var existing))
        {
            Remove(existing);
        }

        if (bytes > maxBytes)
        {
            return;
        }
        while (_recent.First is { } oldest && (_entries.Count >= maxEntries || _bytes + bytes > maxBytes))
        {
            Remove(oldest);
        }

        _entries[key] = _recent.AddLast(new Entry(key, value, bytes, timeProvider.GetTimestamp()));
        _bytes += bytes;
    }

    public void Clear()
    {
        _entries.Clear();
        _recent.Clear();
        _bytes = 0;
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        _entries.Remove(node.Value.Key);
        _recent.Remove(node);
        _bytes -= node.Value.Bytes;
    }
}
