using Beutl.Collections;
using Beutl.Framework;
using Beutl.Media;
using Beutl.Rendering;

namespace Beutl.Operation;

public interface ISourceOperator : IAffectsRender
{
    bool IsEnabled { get; }

    ICoreList<IAbstractProperty> Properties { get; }
}

public class SourceOperator : Hierarchical, ISourceOperator
{
    public static readonly CoreProperty<bool> IsEnabledProperty;
    private bool _isEnabled = true;

    static SourceOperator()
    {
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

    public ICoreList<IAbstractProperty> Properties { get; } = new CoreList<IAbstractProperty>();

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

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
            case ISourceHandler handler:
                handler.Handle(context.FlowRenderables, context.Clock);
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

    protected void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
    }
}
