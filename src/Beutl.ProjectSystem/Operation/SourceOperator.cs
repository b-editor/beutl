using Beutl.Collections;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Operation;

public interface ISourceOperator : IAffectsRender
{
    bool IsEnabled { get; }

    ICoreList<IPropertyAdapter> Properties { get; }
}

[DummyType(typeof(DummySourceOperator))]
public class SourceOperator : Hierarchical, ISourceOperator
{
    public static readonly CoreProperty<ICoreList<IPropertyAdapter>> PropertiesProperty;
    public static readonly CoreProperty<bool> IsEnabledProperty;
    private bool _isEnabled = true;

    static SourceOperator()
    {
        PropertiesProperty = ConfigureProperty<ICoreList<IPropertyAdapter>, SourceOperator>(nameof(Properties))
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
    public ICoreList<IPropertyAdapter> Properties { get; } = new CoreList<IPropertyAdapter>();

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
