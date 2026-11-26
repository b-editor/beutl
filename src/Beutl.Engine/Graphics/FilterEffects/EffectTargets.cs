using System.Collections;

namespace Beutl.Graphics.Effects;

/// <summary>
/// The targets a custom effect reads and writes. The list owns its elements: whatever drops an element
/// disposes it, and <see cref="DetachAt"/> is the only way to take one out alive.
/// </summary>
/// <remarks>
/// Ownership enters through <see cref="Add"/>, <see cref="Insert"/>, <see cref="AddRange"/>,
/// <see cref="InsertRange"/> and the indexer setter. The setter disposes the element it replaces,
/// <see cref="Remove"/> and <see cref="RemoveAt"/> dispose what they remove, and <see cref="Clear"/> and
/// <see cref="Dispose"/> dispose everything. A target belongs to one list at a time: inserting one that any
/// list holds throws, so detach it first. <see cref="EffectTarget.Dispose"/> is idempotent, so disposing a
/// target yourself before replacing it is harmless.
/// </remarks>
public sealed class EffectTargets : IList<EffectTarget>, IDisposable
{
    private readonly List<EffectTarget> _targets = [];
    private List<ClippedTextOccurrence>? _clippedText;

    /// <summary>Creates an empty list.</summary>
    public EffectTargets()
    {
    }

    /// <summary>Creates a list of clones of the elements of <paramref name="obj"/>, which keeps its own.</summary>
    public EffectTargets(EffectTargets obj)
    {
        foreach (EffectTarget item in obj)
        {
            Add(item.Clone());
        }
        if (obj._clippedText is { Count: > 0 } clipped)
            _clippedText = [.. clipped];
    }

    /// <summary>Gets or sets the target at <paramref name="index"/>.</summary>
    /// <remarks>
    /// Setting disposes the previous element unless it is the same instance; <see cref="DetachAt"/> it first to
    /// keep it.
    /// </remarks>
    public EffectTarget this[int index]
    {
        get => _targets[index];
        set
        {
            EffectTarget previous = _targets[index];
            if (ReferenceEquals(previous, value))
                return;

            Take(value);
            _targets[index] = value;
            Release(previous).Dispose();
        }
    }

    public int Count => _targets.Count;

    internal IReadOnlyList<ClippedTextOccurrence> ClippedText
        => _clippedText is { } clipped ? clipped : Array.Empty<ClippedTextOccurrence>();

    internal void SetClippedText(IReadOnlyList<ClippedTextOccurrence> clipped)
    {
        if (clipped.Count > 0)
            _clippedText = [.. clipped];
    }

    internal void ClipTextAt(int index, TextPartTag tag)
    {
        var clipped = _clippedText ??= [];
        int slot = FindClippedTextAfter(index);
        // Text on Path clips glyphs in reverse. Move a marker for the same block back across
        // consecutive clipped glyphs so that a whole block contributes only one marker.
        if (slot < clipped.Count && clipped[slot] is { TargetIndex: var next, Tag: var previous }
            && next == index + 1 && previous.OccurrenceId == tag.OccurrenceId && previous.LayoutRevision == tag.LayoutRevision)
        {
            clipped[slot] = new ClippedTextOccurrence(index, tag);
        }
        else
        {
            clipped.Insert(slot, new ClippedTextOccurrence(index, tag));
        }
        RemoveAt(index);
    }

    public bool IsReadOnly => false;

    /// <summary>Unions the <see cref="EffectTarget.Bounds"/> of every element.</summary>
    public Rect CalculateBounds()
    {
        Rect bounds = default;
        for (int index = 0; index < _targets.Count; index++)
            bounds = bounds.Union(_targets[index].Bounds);
        return bounds;
    }

    /// <summary>The union of the complete bounds of every element, which are its bounds when it holds all of its value.</summary>
    internal Rect CalculateCompleteBounds()
    {
        Rect bounds = default;
        for (int index = 0; index < _targets.Count; index++)
            bounds = bounds.Union(_targets[index].CompleteBounds ?? _targets[index].Bounds);
        return bounds;
    }

    /// <summary>Creates a list of clones of every element.</summary>
    public EffectTargets Clone() => new(this);

    /// <summary>Appends <paramref name="item"/> and takes ownership of it.</summary>
    /// <exception cref="InvalidOperationException">A list already holds <paramref name="item"/>.</exception>
    public void Add(EffectTarget item)
    {
        Take(item);
        _targets.Add(item);
    }

    /// <summary>Appends the targets in <paramref name="collection"/>; see <see cref="InsertRange"/>.</summary>
    public void AddRange(IEnumerable<EffectTarget> collection) => InsertRange(_targets.Count, collection);

    /// <summary>Disposes every element and empties the list.</summary>
    public void Clear()
    {
        _clippedText?.Clear();
        for (int i = _targets.Count - 1; i >= 0; i--)
        {
            EffectTarget target = _targets[i];
            _targets.RemoveAt(i);
            Release(target).Dispose();
        }
    }

    public bool Contains(EffectTarget item) => _targets.Contains(item);
    public void CopyTo(EffectTarget[] array, int arrayIndex) => _targets.CopyTo(array, arrayIndex);
    /// <summary>Gets a struct enumerator, so a <see langword="foreach"/> over this list allocates nothing.</summary>
    public List<EffectTarget>.Enumerator GetEnumerator() => _targets.GetEnumerator();

    IEnumerator<EffectTarget> IEnumerable<EffectTarget>.GetEnumerator()
        => ((IEnumerable<EffectTarget>)_targets).GetEnumerator();
    public int IndexOf(EffectTarget item) => _targets.IndexOf(item);

    /// <summary>Inserts <paramref name="item"/> at <paramref name="index"/> and takes ownership of it.</summary>
    /// <exception cref="InvalidOperationException">A list already holds <paramref name="item"/>.</exception>
    public void Insert(int index, EffectTarget item)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _targets.Count);
        Take(item);
        _targets.Insert(index, item);
        ShiftClippedText(index, 1);
    }

    /// <summary>Inserts the targets in <paramref name="collection"/> at <paramref name="index"/> and takes ownership of them.</summary>
    /// <remarks>
    /// Another <see cref="EffectTargets"/> is moved and left empty, so disposing it afterwards releases nothing.
    /// Any other sequence is staged first; if it fails or is refused, nothing is inserted.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="collection"/> is this list or yields a target some list already holds.
    /// </exception>
    public void InsertRange(int index, IEnumerable<EffectTarget> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _targets.Count);

        if (collection is EffectTargets source)
        {
            if (ReferenceEquals(source, this))
                throw new InvalidOperationException("A target list cannot be inserted into itself.");

            foreach (EffectTarget item in source._targets)
                item.Owner = this;
            InsertClippedText(index, source, removedCount: 0);
            _targets.InsertRange(index, source._targets);
            source._targets.Clear();
            return;
        }

        var staged = new List<EffectTarget>();
        try
        {
            foreach (EffectTarget item in collection)
            {
                Take(item);
                staged.Add(item);
            }
        }
        catch
        {
            foreach (EffectTarget item in staged)
                item.Owner = null;
            throw;
        }

        _targets.InsertRange(index, staged);
        ShiftClippedText(index, staged.Count);
    }

    /// <summary>Removes <paramref name="item"/> and disposes it.</summary>
    /// <returns><see langword="false"/>, touching nothing, when the list does not hold <paramref name="item"/>.</returns>
    public bool Remove(EffectTarget item)
    {
        int index = _targets.IndexOf(item);
        if (index < 0)
            return false;

        RemoveAt(index);
        return true;
    }

    /// <summary>Removes and disposes the element at <paramref name="index"/>.</summary>
    public void RemoveAt(int index)
    {
        EffectTarget target = _targets[index];
        _targets.RemoveAt(index);
        ShiftClippedText(index, -1);
        Release(target).Dispose();
    }

    /// <summary>Removes the element at <paramref name="index"/> without disposing it; the caller now owns it.</summary>
    public EffectTarget DetachAt(int index)
    {
        EffectTarget target = _targets[index];
        _targets.RemoveAt(index);
        ShiftClippedText(index, -1);
        return Release(target);
    }

    internal void ReplaceAt(int index, EffectTargets replacement)
    {
        EffectTarget previous = _targets[index];
        foreach (EffectTarget target in replacement._targets)
            target.Owner = this;
        InsertClippedText(index, replacement, removedCount: 1);
        _targets.RemoveAt(index);
        _targets.InsertRange(index, replacement._targets);
        replacement._targets.Clear();
        Release(previous).Dispose();
    }

    private void InsertClippedText(int index, EffectTargets source, int removedCount)
    {
        int slot = FindClippedTextAfter(index);
        ShiftClippedText(index, source.Count - removedCount);
        if (source._clippedText is not { Count: > 0 } incoming)
            return;

        var clipped = _clippedText ??= [];
        foreach (ClippedTextOccurrence entry in incoming)
            clipped.Insert(slot++, entry with { TargetIndex = index + entry.TargetIndex });
        incoming.Clear();
    }

    private int FindClippedTextAfter(int index)
    {
        int slot = _clippedText?.Count ?? 0;
        while (slot > 0 && _clippedText![slot - 1].TargetIndex > index)
            slot--;
        return slot;
    }

    private void ShiftClippedText(int index, int delta)
    {
        if (_clippedText is not { Count: > 0 } clipped || delta == 0)
            return;
        for (int slot = clipped.Count - 1; slot >= 0 && clipped[slot].TargetIndex > index; slot--)
        {
            ClippedTextOccurrence entry = clipped[slot];
            clipped[slot] = entry with { TargetIndex = entry.TargetIndex + delta };
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_targets).GetEnumerator();
    /// <summary>Disposes every element and empties the list.</summary>
    public void Dispose() => Clear();

    private void Take(EffectTarget item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Owner is not null)
        {
            throw new InvalidOperationException(ReferenceEquals(item.Owner, this)
                ? "The list already owns this target."
                : "Another list owns this target; detach it first.");
        }

        item.Owner = this;
    }

    private static EffectTarget Release(EffectTarget item)
    {
        item.Owner = null;
        return item;
    }
}
