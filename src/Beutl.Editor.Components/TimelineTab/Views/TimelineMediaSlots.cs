using System.Diagnostics.CodeAnalysis;

namespace Beutl.Editor.Components.TimelineTab.Views;

internal sealed class TimelineMediaSlots<T>(Action<T>? release = null)
{
    private readonly Dictionary<int, T> _base = new();
    private readonly Dictionary<int, T> _preview = new();

    public void Set(int index, T value, bool preview = false)
    {
        var slots = preview ? _preview : _base;
        if (release != null && slots.TryGetValue(index, out var previous))
            release(previous);
        slots[index] = value;
    }

    public bool TryGetValue(int index, [MaybeNullWhen(false)] out T value)
        => _preview.TryGetValue(index, out value) || _base.TryGetValue(index, out value);

    public List<int> GetMissingIndices(int start, int end)
    {
        var missing = new List<int>();
        for (int i = start; i <= end; i++)
        {
            if (!TryGetValue(i, out _))
                missing.Add(i);
        }
        return missing;
    }

    public void ClearPreview() => Clear(_preview);

    public void Clear()
    {
        Clear(_base);
        ClearPreview();
    }

    private void Clear(Dictionary<int, T> slots)
    {
        if (release != null)
        {
            foreach (T item in slots.Values)
                release(item);
        }
        slots.Clear();
    }
}
