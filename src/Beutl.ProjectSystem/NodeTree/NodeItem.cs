using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Extensibility;
using Beutl.Media;

namespace Beutl.NodeTree;

public abstract class NodeItem : Hierarchical
{
    public static readonly CoreProperty<DisplayAttribute?> DisplayProperty;
    private DisplayAttribute? _display;

    static NodeItem()
    {
        DisplayProperty = ConfigureProperty<DisplayAttribute?, NodeItem>(nameof(Display))
            .Accessor(o => o.Display, (o, v) => o.Display = v)
            .Register();
    }

    [NotAutoSerialized]
    [NotTracked]
    public DisplayAttribute? Display
    {
        get => _display;
        set => SetAndRaise(DisplayProperty, ref _display, value);
    }

    public event EventHandler? TopologyChanged;

    protected void RaiseTopologyChanged()
    {
        TopologyChanged?.Invoke(this, EventArgs.Empty);
    }
}

public class NodeItem<T> : NodeItem, INodeItem, ISupportSetValueNodeItem
{
    public IPropertyAdapter<T>? Property { get; protected set; }

    // レンダリング時に変更されるので、変更通知は必要ない
    public T? Value { get; set; }

    public virtual Type? AssociatedType => typeof(T);

    public event EventHandler? Edited;

    public virtual void PreEvaluate(EvaluationContext context)
    {
        if (Property is { } property)
        {
            if (property is IAnimatablePropertyAdapter<T> { Animation: IAnimation<T> animation })
            {
                Value = animation.GetAnimatedValue(context.Renderer.Time);
            }
            else
            {
                Value = property.GetValue();
            }
        }
    }

    public virtual void Evaluate(EvaluationContext context)
    {
    }

    public virtual void PostEvaluate(EvaluationContext context)
    {
    }

    protected void RaiseEdited(EventArgs args)
    {
        Edited?.Invoke(this, args);
    }

    IPropertyAdapter? INodeItem.Property => Property;

    object? INodeItem.Value => Value;

    void ISupportSetValueNodeItem.SetThrough(INodeItem nodeItem)
    {
        if (nodeItem is NodeItem<T> t)
        {
            Value = t.Value;
        }
    }
}
