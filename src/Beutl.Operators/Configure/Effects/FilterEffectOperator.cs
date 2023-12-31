using System.Runtime.CompilerServices;

using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Operation;

namespace Beutl.Operators.Configure.Effects;

public abstract class FilterEffectOperator<T> : ConfigureOperator<Drawable, T>
    where T : FilterEffect, new()
{
    private readonly ConditionalWeakTable<Drawable, CombinedFilterEffect> _table = [];

    protected override void PreProcess(Drawable target, T value)
    {
        value.IsEnabled = IsEnabled;
    }

    protected override void Process(Drawable target, T value)
    {
        CombinedFilterEffect composed = _table.GetValue(target, _ => new CombinedFilterEffect());
        if (target.FilterEffect != composed)
        {
            composed.Second = value;
            composed.First = target.FilterEffect;
            target.FilterEffect = composed;
        }
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        _table.Clear();
    }
}
