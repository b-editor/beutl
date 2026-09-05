using System.Collections;

namespace Beutl.Graphics.Effects;

public sealed class EffectTargets : IList<EffectTarget>, IDisposable
{
    private readonly List<EffectTarget> _targets = [];

    public EffectTargets()
    {
    }

    public EffectTargets(EffectTargets obj)
    {
        foreach (EffectTarget item in obj)
        {
            Add(item.Clone());
        }
    }

    public EffectTarget this[int index] { get => ((IList<EffectTarget>)_targets)[index]; set => ((IList<EffectTarget>)_targets)[index] = value; }

    public int Count => ((ICollection<EffectTarget>)_targets).Count;

    public bool IsReadOnly => ((ICollection<EffectTarget>)_targets).IsReadOnly;

    public Rect CalculateBounds()
    {
        Rect bounds = default;
        for (int index = 0; index < _targets.Count; index++)
            bounds = bounds.Union(_targets[index].Bounds);
        return bounds;
    }

    public EffectTargets Clone() => new(this);
    public void Add(EffectTarget item) => ((ICollection<EffectTarget>)_targets).Add(item);
    public void AddRange(IEnumerable<EffectTarget> collection) => _targets.AddRange(collection);
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
    public void Insert(int index, EffectTarget item) => _targets.Insert(index, item);
    public void InsertRange(int index, IEnumerable<EffectTarget> collection) => _targets.InsertRange(index, collection);
    public bool Remove(EffectTarget item) => ((ICollection<EffectTarget>)_targets).Remove(item);
    public void RemoveAt(int index) => ((IList<EffectTarget>)_targets).RemoveAt(index);
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_targets).GetEnumerator();
    public void Dispose()
    {
        for (int i = _targets.Count - 1; i >= 0; i--)
        {
            _targets[i].Dispose();
            _targets.RemoveAt(i);
        }
    }
}
