using System.Runtime.CompilerServices;

using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.Operators.Configure.Transform;

#pragma warning disable IDE0065
using Transform = Graphics.Transformation.Transform;

public abstract class TransformOperator<T> : ConfigureOperator<Drawable, T>
    where T : Transform, new()
{
    private readonly ConditionalWeakTable<Drawable, MultiTransform> _table = new();

    protected override void PreProcess(Drawable target, T value)
    {
        value.IsEnabled = IsEnabled;
    }

    protected override void Process(Drawable target, T value)
    {
        MultiTransform multi = _table.GetValue(target, _ => new MultiTransform());
        if (target.Transform != multi)
        {
            multi.Left = value;
            multi.Right = target.Transform;
            target.Transform = multi;
        }
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        _table.Clear();
    }
}
