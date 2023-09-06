using System.Runtime.CompilerServices;

using Beutl.Animation;
using Beutl.Collections;
using Beutl.Rendering;
using Beutl.Styling;

namespace Beutl.Operation;

public abstract class SourceStyler : StylingOperator, ISourceTransformer
{
    private readonly ConditionalWeakTable<Renderable, IStyleInstance> _table = new();

    internal ConditionalWeakTable<Renderable, IStyleInstance> Table => _table;

    public virtual void Transform(IList<Renderable> value, IClock clock)
    {
        if (IsEnabled)
        {
            for (int i = 0; i < value.Count; i++)
            {
                Renderable renderable = value[i];
                OnPreSelect(renderable);
                IStyleInstance? instance = GetInstance(value[i]);

                if (instance != null)
                {
                    ApplyStyle(instance, renderable, clock);
                }

                OnPostSelect(renderable);
            }
        }
    }

    protected virtual void OnPreSelect(Renderable? value)
    {
    }

    protected virtual void OnPostSelect(Renderable? value)
    {
    }

    protected virtual IStyleInstance? GetInstance(Renderable value)
    {
        Type type = value.GetType();
        if (_table.TryGetValue(value, out IStyleInstance? styleInstance))
        {
            return styleInstance;
        }
        else
        {
            if (type.IsAssignableTo(Style.TargetType) && value is ICoreObject coreObj)
            {
                IStyleInstance instance = Style.Instance(coreObj);
                _table.AddOrUpdate(value, instance);
                return instance;
            }
            else
            {
                return null;
            }
        }
    }

    protected virtual void ApplyStyle(IStyleInstance instance, Renderable value, IClock clock)
    {
        instance.Begin();
        instance.Apply(clock);
        instance.End();
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        using var instances = _table.Select(x => x.Value).ToPooledArray();
        _table.Clear();

        foreach (IStyleInstance item in instances)
        {
            item.Dispose();
        }
    }
}
