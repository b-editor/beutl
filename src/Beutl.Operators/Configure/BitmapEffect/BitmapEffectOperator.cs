using System.Runtime.CompilerServices;

using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Operation;

namespace Beutl.Operators.Configure.BitmapEffect;

#pragma warning disable IDE0065
using BitmapEffect = Graphics.Effects.BitmapEffect;

public abstract class BitmapEffectOperator<T> : ConfigureOperator<Drawable, T>, ISourceTransformer
    where T : BitmapEffect, new()
{
    private readonly ConditionalWeakTable<Drawable, ComposedBitmapEffect> _table = new();

    protected override void PreProcess(Drawable target, T value)
    {
        value.IsEnabled = IsEnabled;
    }

    protected override void Process(Drawable target, T value)
    {
        ComposedBitmapEffect composed = _table.GetValue(target, _ => new ComposedBitmapEffect());
        if (target.Effect != composed)
        {
            composed.Second = value;
            composed.First = target.Effect;
            target.Effect = composed;
        }
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        _table.Clear();
    }
}
