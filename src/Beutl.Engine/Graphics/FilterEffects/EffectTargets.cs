using System.Collections;

namespace Beutl.Graphics.Effects;

/// <summary>
/// The intermediate targets a custom effect reads and writes. The list owns every <see cref="EffectTarget"/>
/// it holds, and every member that drops an element releases that element's render target or pooled lease.
/// </summary>
/// <remarks>
/// <para>
/// Ownership follows the list, not the element. It transfers into the list through <see cref="Add"/>,
/// <see cref="AddRange"/>, <see cref="Insert"/>, <see cref="InsertRange"/> and the indexer setter, and it
/// leaves the list alive only through <see cref="DetachAt"/>. Every other member that drops an element disposes
/// it: the indexer setter disposes the element it replaces unless it is the same instance, <see cref="Remove"/>
/// and <see cref="RemoveAt"/> dispose the element they remove, and <see cref="Clear"/> and <see cref="Dispose"/>
/// dispose them all.
/// </para>
/// <para>
/// A hand-written <c>targets[i] = replacement;</c> is therefore safe, and an explicit <c>Dispose()</c> of the
/// previous element around it is redundant but harmless, because <see cref="EffectTarget.Dispose"/> may be
/// called more than once. Hold a target in one list at a time: adding the same instance twice makes the first
/// removal dispose the other slot's element as well. The <c>CustomFilterEffectContext.ForEach</c> overloads
/// build on these rules and are the convenient way to replace or expand targets in place.
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
    /// <remarks>
    /// An empty element has nothing to clone, so <see cref="EffectTarget.Clone"/> returns that element itself
    /// and both lists hold the same instance. Disposing it through either list releases nothing and only resets
    /// its bounds.
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
    /// Setting takes ownership of the new value and disposes the element it replaces, unless that element is
    /// the new value itself. Call <see cref="DetachAt"/> first to keep the previous element alive.
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

    /// <summary>
    /// Creates a list holding an <see cref="EffectTarget.Clone"/> of every element. The clones belong to the
    /// new list; this list keeps its own elements. See the copy constructor for empty elements.
    /// </summary>
    public EffectTargets Clone() => new(this);
    /// <summary>Appends <paramref name="item"/> and takes ownership of it.</summary>
    public void Add(EffectTarget item) => _targets.Add(item);
    /// <summary>Appends every target in <paramref name="collection"/> and takes ownership of them.</summary>
    public void AddRange(IEnumerable<EffectTarget> collection) => _targets.AddRange(collection);

    /// <summary>Disposes every element, last to first, and empties the list.</summary>
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
    public void Insert(int index, EffectTarget item) => _targets.Insert(index, item);
    /// <summary>Inserts every target in <paramref name="collection"/> at <paramref name="index"/> and takes ownership of them.</summary>
    public void InsertRange(int index, IEnumerable<EffectTarget> collection) => _targets.InsertRange(index, collection);

    /// <summary>Removes <paramref name="item"/> and disposes it.</summary>
    /// <returns>
    /// <see langword="true"/> when the list held <paramref name="item"/>. A target the list does not hold is
    /// left untouched.
    /// </returns>
    public bool Remove(EffectTarget item)
    {
        int index = _targets.IndexOf(item);
        if (index < 0)
            return false;

        RemoveAt(index);
        return true;
    }

    /// <summary>Removes the element at <paramref name="index"/> and disposes it.</summary>
    /// <remarks>Use <see cref="DetachAt"/> to take the element out alive instead.</remarks>
    public void RemoveAt(int index)
    {
        EffectTarget target = _targets[index];
        _targets.RemoveAt(index);
        target.Dispose();
    }

    /// <summary>Removes the element at <paramref name="index"/> without disposing it and returns it.</summary>
    /// <remarks>
    /// Ownership passes to the caller, who must dispose the returned target or hand it to a list that will.
    /// </remarks>
    public EffectTarget DetachAt(int index)
    {
        EffectTarget target = _targets[index];
        _targets.RemoveAt(index);
        return target;
    }

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_targets).GetEnumerator();
    /// <summary>Disposes every element, last to first, and empties the list.</summary>
    public void Dispose() => Clear();
}
