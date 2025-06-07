using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dオブジェクトのグループ
/// 複数の3Dオブジェクトを階層化して管理
/// </summary>
[Display(Name = "Group3D")]
public sealed class Drawable3DGroup : Drawable3D
{
    public static readonly CoreProperty<Drawable3Ds> ChildrenProperty;
    private readonly Drawable3Ds _children = [];

    static Drawable3DGroup()
    {
        ChildrenProperty = ConfigureProperty<Drawable3Ds, Drawable3DGroup>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .Register();

        AffectsRender<Drawable3DGroup>(ChildrenProperty);
        Hierarchy<Drawable3DGroup>(ChildrenProperty);
    }

    public Drawable3DGroup()
    {
        _children.Invalidated += (_, e) => RaiseInvalidated(e);
        _children.Attached += HierarchicalChildren.Add;
        _children.Detached += item => HierarchicalChildren.Remove(item);
    }

    /// <summary>
    /// 子オブジェクトのコレクション
    /// </summary>
    [NotAutoSerialized]
    public Drawable3Ds Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    // グループはメッシュを持たないため、ダミーメッシュを返す
    public override I3DMeshResource Mesh => DummyMeshResource.Instance;

    protected override void RenderCore3D(I3DCanvas canvas)
    {
        // 子オブジェクトをレンダリング
        foreach (var child in _children)
        {
            if (child.IsVisible)
            {
                child.Render3D(canvas);
            }
        }
    }

    public override BoundingBox GetBounds3D()
    {
        if (_children.Count == 0)
            return BoundingBox.Empty;

        BoundingBox bounds = BoundingBox.Empty;
        bool hasBounds = false;

        foreach (var child in _children)
        {
            if (!child.IsVisible)
                continue;

            var childBounds = child.GetBounds3D();
            if (childBounds.IsEmpty)
                continue;

            // 子オブジェクトの変換を適用
            var transformedBounds = childBounds.Transform(((I3DRenderableObject)child).Transform);

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

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Children), Children);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<Drawable3Ds>(nameof(Children)) is { } children)
        {
            Children = children;
        }
    }

    /// <summary>
    /// 子オブジェクトを追加
    /// </summary>
    public void AddChild(Drawable3D child)
    {
        _children.Add(child);
    }

    /// <summary>
    /// 子オブジェクトを削除
    /// </summary>
    public bool RemoveChild(Drawable3D child)
    {
        return _children.Remove(child);
    }

    /// <summary>
    /// 子オブジェクトをクリア
    /// </summary>
    public void ClearChildren()
    {
        _children.Clear();
    }

    /// <summary>
    /// 指定したインデックスに子オブジェクトを挿入
    /// </summary>
    public void InsertChild(int index, Drawable3D child)
    {
        _children.Insert(index, child);
    }

    /// <summary>
    /// 子オブジェクトの階層を深さ優先で列挙
    /// </summary>
    public IEnumerable<Drawable3D> EnumerateDescendants()
    {
        foreach (var child in _children)
        {
            yield return child;

            if (child is Drawable3DGroup group)
            {
                foreach (var descendant in group.EnumerateDescendants())
                {
                    yield return descendant;
                }
            }
        }
    }

    /// <summary>
    /// 指定した型の子オブジェクトを検索
    /// </summary>
    public IEnumerable<T> FindChildren<T>() where T : Drawable3D
    {
        foreach (var child in _children)
        {
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            if (child is Drawable3DGroup group)
            {
                foreach (var descendant in group.FindChildren<T>())
                {
                    yield return descendant;
                }
            }
        }
    }

    /// <summary>
    /// 名前で子オブジェクトを検索
    /// </summary>
    public Drawable3D? FindChildByName(string name)
    {
        foreach (var child in _children)
        {
            // 名前プロパティがあれば比較（実装依存）
            if (child.GetType().GetProperty("Name")?.GetValue(child)?.ToString() == name)
            {
                return child;
            }

            if (child is Drawable3DGroup group)
            {
                var found = group.FindChildByName(name);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }
}
