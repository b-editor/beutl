using Beutl.Collections;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Rendering;
using Beutl.Serialization;

namespace Beutl.Operation;

public interface ISourceOperator : IAffectsRender
{
    bool IsEnabled { get; }

    ICoreList<IAbstractProperty> Properties { get; }
}

[DummyType(typeof(DummySourceOperator))]
public class SourceOperator : Hierarchical, ISourceOperator
{
    public static readonly CoreProperty<ICoreList<IAbstractProperty>> PropertiesProperty;
    public static readonly CoreProperty<bool> IsEnabledProperty;
    private bool _isEnabled = true;

    static SourceOperator()
    {
        PropertiesProperty = ConfigureProperty<ICoreList<IAbstractProperty>, SourceOperator>(nameof(Properties))
            .Accessor(o => o.Properties)
            .Register();

        IsEnabledProperty = ConfigureProperty<bool, SourceOperator>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Register();
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetAndRaise(IsEnabledProperty, ref _isEnabled, value))
            {
                Invalidated?.Invoke(this, new RenderInvalidatedEventArgs(this, nameof(IsEnabled)));
            }
        }
    }

    [NotAutoSerialized]
    public ICoreList<IAbstractProperty> Properties { get; } = new CoreList<IAbstractProperty>();

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public virtual EvaluationTarget GetEvaluationTarget()
    {
        return EvaluationTarget.Unknown;
    }

    public virtual void InitializeForContext(OperatorEvaluationContext context)
    {
    }

    public virtual void UninitializeForContext(OperatorEvaluationContext context)
    {
    }

    public virtual void Evaluate(OperatorEvaluationContext context)
    {
        switch (this)
        {
            case ISourceTransformer selector:
                selector.Transform(context.FlowRenderables, context.Clock);
                break;
            case ISourcePublisher source:
                if (source.Publish(context.Clock) is Renderable renderable)
                {
                    context.AddFlowRenderable(renderable);
                }
                break;
            default:
                break;
        }
    }

    public virtual void Enter()
    {
    }

    public virtual void Exit()
    {
    }

    public virtual bool HasOriginalLength()
    {
        return false;
    }
    
    public virtual bool TryGetOriginalLength(out TimeSpan timeSpan)
    {
        timeSpan = default;
        return false;
    }

    public virtual IRecordableCommand? OnSplit(bool backward, TimeSpan startDelta, TimeSpan lengthDelta)
    {
        return null;
    }

    protected void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
    }
}
