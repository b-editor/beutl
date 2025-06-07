using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Drawable3Dオブジェクトのコレクション
/// </summary>
public class Drawable3Ds : AffectsRenders<Drawable3D>
{
    /// <summary>
    /// Z-インデックスでソート
    /// </summary>
    public void SortByZIndex()
    {
        var sorted = this.OrderBy(item => item.ZIndex).ToList();
        Clear();
        AddRange(sorted);
    }

    /// <summary>
    /// 表示可能なオブジェクトのみを取得
    /// </summary>
    public IEnumerable<Drawable3D> GetVisible()
    {
        return this.Where(item => item.IsVisible);
    }

    /// <summary>
    /// 指定した型のオブジェクトを取得
    /// </summary>
    public IEnumerable<T> OfType<T>() where T : Drawable3D
    {
        return this.Where(item => item is T).Cast<T>();
    }

    /// <summary>
    /// バウンディングボックスを計算
    /// </summary>
    public BoundingBox GetBounds()
    {
        if (Count == 0)
            return BoundingBox.Empty;

        BoundingBox bounds = BoundingBox.Empty;
        bool hasBounds = false;

        foreach (var item in GetVisible())
        {
            var itemBounds = item.GetBounds3D();
            if (itemBounds.IsEmpty)
                continue;

            var transformedBounds = itemBounds.Transform(((I3DRenderableObject)item).Transform);

            if (!hasBounds)
            {
                bounds = transformedBounds;
                hasBounds = true;
            }
            else
            {
                bounds = bounds.Union(transformedBounds);
            }
        }

        return bounds;
    }
}
