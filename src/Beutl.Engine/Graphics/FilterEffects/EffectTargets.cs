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
        return this.Aggregate<EffectTarget, Rect>(default, (x, y) => x.Union(y.Bounds));
    }
    public EffectTargets Clone() => new(this);
    public void Add(EffectTarget item) => ((ICollection<EffectTarget>)_targets).Add(item);
    public void AddRange(IEnumerable<EffectTarget> collection) => _targets.AddRange(collection);
    public void Clear() => ((ICollection<EffectTarget>)_targets).Clear();
    public bool Contains(EffectTarget item) => ((ICollection<EffectTarget>)_targets).Contains(item);
    public void CopyTo(EffectTarget[] array, int arrayIndex) => ((ICollection<EffectTarget>)_targets).CopyTo(array, arrayIndex);
    public IEnumerator<EffectTarget> GetEnumerator() => ((IEnumerable<EffectTarget>)_targets).GetEnumerator();
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
