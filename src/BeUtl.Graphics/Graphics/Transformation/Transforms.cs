using System.Collections.ObjectModel;

namespace BeUtl.Graphics.Transformation;

public sealed class Transforms : Collection<Transform>
{
    private readonly Drawable _drawable;

    public Transforms(Drawable drawable)
    {
        _drawable = drawable;
    }

    protected override void ClearItems()
    {
        base.ClearItems();
        _drawable.InvalidateVisual();
    }

    protected override void InsertItem(int index, Transform item)
    {
        base.InsertItem(index, item);
        item.Parent = _drawable;
        _drawable.InvalidateVisual();
    }

    protected override void RemoveItem(int index)
    {
        this[index].Parent = null;
        base.RemoveItem(index);
        _drawable.InvalidateVisual();
    }

    protected override void SetItem(int index, Transform item)
    {
        base.SetItem(index, item);
        item.Parent = _drawable;
        _drawable.InvalidateVisual();
    }

    public void AddRange(IEnumerable<Transform> items)
    {
        if (items is IList<Transform> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Add(list[i]);
            }
        }
        else if (items is Transform[] array)
        {
            for (int i = 0; i < array.Length; i++)
            {
                Add(array[i]);
            }
        }
        else
        {
            foreach (Transform? item in items)
            {
                Add(item);
            }
        }
    }

    public Matrix Calculate()
    {
        Transforms list = this;
        Matrix value = Matrix.Identity;

        for (int i = 0; i < list.Count; i++)
        {
            Transform item = list[i];
            value = item.Value * value;
        }

        return value;
    }
}
