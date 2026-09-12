using System.Collections;

namespace Beutl.Graphics.Effects;

/// <summary>
/// The intermediate targets a custom effect reads and writes. The list owns every <see cref="EffectTarget"/>
/// it holds: <see cref="Dispose"/> releases each element's render target or pooled lease.
/// </summary>
/// <remarks>
/// <para>
/// Ownership follows the list, not the element. It transfers into the list through <see cref="Add"/>,
/// <see cref="AddRange"/>, <see cref="Insert"/> and <see cref="InsertRange"/>, and it transfers back to the
/// caller through every member that drops an element without disposing it: the indexer setter,
/// <see cref="Clear"/>, <see cref="Remove"/> and <see cref="RemoveAt"/>. An element removed that way keeps its
/// render target allocated until whoever now holds it disposes it.
/// </para>
/// <para>
/// A hand-written <c>targets[i] = replacement;</c> therefore leaks the previous element unless the caller
/// disposes it first. <see cref="CustomFilterEffectContext.ForEach(Func{int, EffectTarget, EffectTarget})"/>
/// does that bookkeeping and is the intended way to replace targets in place.
/// </para>
/// </remarks>
public sealed class EffectTargets : IList<EffectTarget>, IDisposable
{
    private readonly List<EffectTarget> _targets = [];

    /// <summary>Creates an empty list.</summary>
    public EffectTargets()
    {
    }

    /// <summary>
    /// Creates a list holding an <see cref="EffectTarget.Clone"/> of every element of <paramref name="obj"/>.
    /// The clones belong to the new list; <paramref name="obj"/> keeps its own elements.
    /// </summary>
    public EffectTargets(EffectTargets obj)
    {
        foreach (EffectTarget item in obj)
        {
            Add(item.Clone());
        }
    }

    /// <summary>Gets or sets the target at <paramref name="index"/>.</summary>
    /// <remarks>
    /// Setting takes ownership of the new value and does <b>not</b> dispose the previous element, which stays
    /// allocated until the caller disposes it. Prefer
    /// <see cref="CustomFilterEffectContext.ForEach(Func{int, EffectTarget, EffectTarget})"/>, which disposes
    /// a replaced target for you.
    /// </remarks>
    public EffectTarget this[int index] { get => ((IList<EffectTarget>)_targets)[index]; set => ((IList<EffectTarget>)_targets)[index] = value; }

    public int Count => ((ICollection<EffectTarget>)_targets).Count;

    public bool IsReadOnly => ((ICollection<EffectTarget>)_targets).IsReadOnly;

    /// <summary>Unions the <see cref="EffectTarget.Bounds"/> of every element.</summary>
    public Rect CalculateBounds()
    {
        Rect bounds = default;
        for (int index = 0; index < _targets.Count; index++)
            bounds = bounds.Union(_targets[index].Bounds);
        return bounds;
    }

    /// <summary>
    /// Creates a list holding an <see cref="EffectTarget.Clone"/> of every element. The clones belong to the
    /// new list; this list keeps its own elements.
    /// </summary>
    public EffectTargets Clone() => new(this);
    /// <summary>Appends <paramref name="item"/> and takes ownership of it.</summary>
    public void Add(EffectTarget item) => ((ICollection<EffectTarget>)_targets).Add(item);
    /// <summary>Appends every target in <paramref name="collection"/> and takes ownership of them.</summary>
    public void AddRange(IEnumerable<EffectTarget> collection) => _targets.AddRange(collection);
    /// <summary>Removes every element without disposing any of them.</summary>
    /// <remarks>Ownership of the removed elements returns to the caller. Use <see cref="Dispose"/> to release them instead.</remarks>
    public void Clear() => ((ICollection<EffectTarget>)_targets).Clear();
    public bool Contains(EffectTarget item) => ((ICollection<EffectTarget>)_targets).Contains(item);
    public void CopyTo(EffectTarget[] array, int arrayIndex) => ((ICollection<EffectTarget>)_targets).CopyTo(array, arrayIndex);
    /// <summary>Gets a struct enumerator, so a <see langword="foreach"/> over this list allocates nothing.</summary>
    /// <remarks>
    /// The interface form below is what the language would otherwise bind to, and it boxes the list's own
    /// struct enumerator. Every recorded effect walks its targets several times per frame, so the box is
    /// paid on the render path; <see cref="Collections.Pooled.PooledList{T}"/> keeps the same split.
    /// </remarks>
    public List<EffectTarget>.Enumerator GetEnumerator() => _targets.GetEnumerator();

    IEnumerator<EffectTarget> IEnumerable<EffectTarget>.GetEnumerator()
        => ((IEnumerable<EffectTarget>)_targets).GetEnumerator();
    public int IndexOf(EffectTarget item) => ((IList<EffectTarget>)_targets).IndexOf(item);
    /// <summary>Inserts <paramref name="item"/> at <paramref name="index"/> and takes ownership of it.</summary>
    public void Insert(int index, EffectTarget item) => _targets.Insert(index, item);
    /// <summary>Inserts every target in <paramref name="collection"/> at <paramref name="index"/> and takes ownership of them.</summary>
    public void InsertRange(int index, IEnumerable<EffectTarget> collection) => _targets.InsertRange(index, collection);
    /// <summary>Removes <paramref name="item"/> without disposing it.</summary>
    /// <remarks>Ownership of <paramref name="item"/> returns to the caller, who must dispose it.</remarks>
    public bool Remove(EffectTarget item) => ((ICollection<EffectTarget>)_targets).Remove(item);
    /// <summary>Removes the element at <paramref name="index"/> without disposing it.</summary>
    /// <remarks>Ownership of the removed element returns to the caller, who must dispose it.</remarks>
    public void RemoveAt(int index) => ((IList<EffectTarget>)_targets).RemoveAt(index);
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_targets).GetEnumerator();
    /// <summary>Disposes every element, last to first, and empties the list.</summary>
    public void Dispose()
    {
        for (int i = _targets.Count - 1; i >= 0; i--)
        {
            _targets[i].Dispose();
            _targets.RemoveAt(i);
        }
    }
}
