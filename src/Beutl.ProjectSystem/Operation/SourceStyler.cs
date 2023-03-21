using System.Runtime.CompilerServices;

using Beutl.Animation;
using Beutl.Rendering;
using Beutl.Styling;

namespace Beutl.Operation;

public abstract class SourceStyler : StylingOperator, ISourceTransformer
{
    private readonly ConditionalWeakTable<Renderable, IStyleInstance> _table = new();

    public virtual void Transform(IList<Renderable> value, IClock clock)
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

    public override void Exit()
    {
        base.Exit();
    }

    protected virtual void OnPreSelect(IRenderable? value)
    {
    }

    protected virtual void OnPostSelect(IRenderable? value)
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
            if (type.IsAssignableTo(Style.TargetType) && value is IStyleable styleable)
            {
                var instance = Style.Instance(styleable);
                _table.AddOrUpdate(value, instance);
                return instance;
            }
            else
            {
                return null;
            }
        }
    }

    protected virtual void ApplyStyle(IStyleInstance instance, IRenderable value, IClock clock)
    {
        instance.IsEnabled = IsEnabled;
        instance.Begin();
        instance.Apply(clock);
        instance.End();
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        foreach (KeyValuePair<Renderable, IStyleInstance> item in _table)
        {
            item.Value.Dispose();
        }

        _table.Clear();
    }
}
