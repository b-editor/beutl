using BeUtl.Media;
using BeUtl.ProjectSystem;

namespace BeUtl.Streaming;

public interface IStreamOperator : IAffectsRender
{
    bool IsEnabled { get; }
}

// PropertyInstanceに依存しない代替案
public class StreamOperator : Element, IStreamOperator
{
    public static readonly CoreProperty<bool> IsEnabledProperty;
    private bool _isEnabled = true;

    static StreamOperator()
    {
        IsEnabledProperty = ConfigureProperty<bool, LayerOperation>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Observability(PropertyObservability.Changed)
            .SerializeName("is-enabled")
            .Register();
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetAndRaise(IsEnabledProperty, ref _isEnabled, value))
            {
                RaiseInvalidated();
            }
        }
    }

    public event EventHandler? Invalidated;

    protected void RaiseInvalidated()
    {
        Invalidated?.Invoke(this, EventArgs.Empty);
    }
}
