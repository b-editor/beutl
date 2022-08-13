using BeUtl.Collections;
using BeUtl.Framework;
using BeUtl.Media;

namespace BeUtl.Streaming;

public interface IStreamOperator : IAffectsRender
{
    bool IsEnabled { get; }

    ICoreList<IAbstractProperty> Properties { get; }
}

// PropertyInstanceに依存しない代替案
public class StreamOperator : Element, IStreamOperator
{
    public static readonly CoreProperty<bool> IsEnabledProperty;
    private bool _isEnabled = true;

    static StreamOperator()
    {
        IsEnabledProperty = ConfigureProperty<bool, StreamOperator>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .PropertyFlags(PropertyFlags.NotifyChanged)
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

    public ICoreList<IAbstractProperty> Properties { get; } = new CoreList<IAbstractProperty>();

    public event EventHandler? Invalidated;

    protected void RaiseInvalidated()
    {
        Invalidated?.Invoke(this, EventArgs.Empty);
    }
}
