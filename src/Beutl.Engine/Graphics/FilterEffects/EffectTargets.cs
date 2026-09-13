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
/// <see cref="Dispose"/> dispose everything. A target belongs to one list at a time; inserting one the list
/// already holds throws. <see cref="EffectTarget.Dispose"/> is idempotent, so disposing a target yourself
/// before replacing it is harmless.
/// </remarks>
public sealed class EffectTargets : IList<EffectTarget>, IDisposable
{
    private readonly List<EffectTarget> _targets = [];

    /// <summary>Creates an empty list.</summary>
    public EffectTargets()
    {
    }

    /// <summary>Creates a list of clones of the elements of <paramref name="obj"/>, which keeps its own.</summary>
    /// <remarks>
    /// An empty target clones as itself, so both lists then hold that instance; disposing it releases nothing.
    /// </remarks>
    public EffectTargets(EffectTargets obj)
    {
        foreach (EffectTarget item in obj)
        {
            Add(item.Clone());
        }
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
            ArgumentNullException.ThrowIfNull(value);
            EffectTarget previous = _targets[index];
            if (ReferenceEquals(previous, value))
                return;

            ThrowIfOwned(value);
            _targets[index] = value;
            previous.Dispose();
        }
    }

    public int Count => _targets.Count;

    public bool IsReadOnly => false;

    /// <summary>Unions the <see cref="EffectTarget.Bounds"/> of every element.</summary>
    public Rect CalculateBounds()
    {
        Rect bounds = default;
        for (int index = 0; index < _targets.Count; index++)
            bounds = bounds.Union(_targets[index].Bounds);
        return bounds;
    }

    /// <summary>Creates a list of clones of every element; see the copy constructor.</summary>
    public EffectTargets Clone() => new(this);
    /// <summary>Appends <paramref name="item"/> and takes ownership of it.</summary>
    /// <exception cref="InvalidOperationException">The list already holds <paramref name="item"/>.</exception>
    public void Add(EffectTarget item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ThrowIfOwned(item);
        _targets.Add(item);
    }
    /// <summary>Appends the targets in <paramref name="collection"/>; see <see cref="InsertRange"/>.</summary>
    public void AddRange(IEnumerable<EffectTarget> collection) => InsertRange(_targets.Count, collection);

    /// <summary>Disposes every element and empties the list.</summary>
    public void Clear()
    {
        for (int i = _targets.Count - 1; i >= 0; i--)
        {
            EffectTarget target = _targets[i];
            _targets.RemoveAt(i);
            target.Dispose();
        }
    }

    public bool Contains(EffectTarget item) => _targets.Contains(item);
    public void CopyTo(EffectTarget[] array, int arrayIndex) => _targets.CopyTo(array, arrayIndex);
    /// <summary>Gets a struct enumerator, so a <see langword="foreach"/> over this list allocates nothing.</summary>
    /// <remarks>
    /// The interface form below is what the language would otherwise bind to, and it boxes the list's own
    /// struct enumerator. Every recorded effect walks its targets several times per frame, so the box is
    /// paid on the render path; <see cref="Collections.Pooled.PooledList{T}"/> keeps the same split.
    /// </remarks>
    public List<EffectTarget>.Enumerator GetEnumerator() => _targets.GetEnumerator();

    IEnumerator<EffectTarget> IEnumerable<EffectTarget>.GetEnumerator()
        => ((IEnumerable<EffectTarget>)_targets).GetEnumerator();
    public int IndexOf(EffectTarget item) => _targets.IndexOf(item);
    /// <summary>Inserts <paramref name="item"/> at <paramref name="index"/> and takes ownership of it.</summary>
    /// <exception cref="InvalidOperationException">The list already holds <paramref name="item"/>.</exception>
    public void Insert(int index, EffectTarget item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _targets.Count);
        ThrowIfOwned(item);
        _targets.Insert(index, item);
    }
    /// <summary>Inserts the targets in <paramref name="collection"/> at <paramref name="index"/> and takes ownership of them.</summary>
    /// <remarks>
    /// Another <see cref="EffectTargets"/> is moved and left empty, so disposing it afterwards releases nothing.
    /// Any other sequence is staged first; if it fails or is refused, nothing is inserted and its targets stay
    /// the caller's.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="collection"/> is this list, repeats a target, or contains one the list already holds.
    /// </exception>
    public void InsertRange(int index, IEnumerable<EffectTarget> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _targets.Count);
        if (ReferenceEquals(collection, this))
            throw new InvalidOperationException("A target list cannot be inserted into itself.");

        if (collection is EffectTargets source)
        {
            ThrowIfAnyOwned(source._targets);
            _targets.InsertRange(index, source._targets);
            source._targets.Clear();
            return;
        }

        var staged = new List<EffectTarget>();
        var seen = new HashSet<EffectTarget>(_targets, ReferenceEqualityComparer.Instance);
        foreach (EffectTarget item in collection)
        {
            ArgumentNullException.ThrowIfNull(item, nameof(collection));
            if (!seen.Add(item))
                throw new InvalidOperationException("The list already owns one of the targets being inserted.");

            staged.Add(item);
        }

        _targets.InsertRange(index, staged);
    }

    // The move path runs per effect per frame on lists of a few targets, so it scans unless the list is large.
    private void ThrowIfAnyOwned(List<EffectTarget> items)
    {
        HashSet<EffectTarget>? owned = _targets.Count > 8
            ? new HashSet<EffectTarget>(_targets, ReferenceEqualityComparer.Instance)
            : null;
        foreach (EffectTarget item in items)
        {
            if (owned?.Contains(item) ?? _targets.Contains(item))
                throw new InvalidOperationException("The list already owns one of the targets being inserted.");
        }
    }

    private void ThrowIfOwned(EffectTarget item)
    {
        if (_targets.Contains(item))
            throw new InvalidOperationException("The list already owns this target.");
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
        target.Dispose();
    }

    /// <summary>Removes the element at <paramref name="index"/> without disposing it; the caller now owns it.</summary>
    public EffectTarget DetachAt(int index)
    {
        EffectTarget target = _targets[index];
        _targets.RemoveAt(index);
        return target;
    }

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_targets).GetEnumerator();
    /// <summary>Disposes every element and empties the list.</summary>
    public void Dispose() => Clear();
}
