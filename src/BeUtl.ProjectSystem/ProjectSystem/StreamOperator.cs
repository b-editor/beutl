using BeUtl.Animation;
using BeUtl.Media;
using BeUtl.Rendering;
using BeUtl.Styling;

namespace BeUtl.ProjectSystem;

// PropertyInstanceに依存しない代替案
public abstract class StreamOperator : Element, IAffectsRender
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

public abstract class StreamSource : StreamOperator
{
    public abstract IRenderable? Publish(IClock clock);
}

public abstract class StreamStyledSource : StreamSource
{
    // 実際にUIで編集するのは'Style.Setters'のみ
    public abstract IStyle Style { get; }

    public IStyleInstance? Instance { get; protected set; }

    public override IRenderable? Publish(IClock clock)
    {
        OnPrePublish();
        IRenderable? renderable = null;

        if (ReferenceEquals(Style, Instance?.Source) || Instance?.Target == null)
        {
            renderable = Activator.CreateInstance(Style.TargetType) as IRenderable;
            if (renderable is IStyleable styleable)
            {
                Instance = Style.Instance(styleable);
            }
            else
            {
                renderable = null;
            }
        }

        if (Instance != null && IsEnabled)
        {
            Instance.Begin();
            Instance.Apply(clock);
            Instance.End();
        }

        OnPostPublish();

        return IsEnabled ? renderable : null;
    }

    protected virtual void OnPrePublish()
    {
    }

    protected virtual void OnPostPublish()
    {
    }
}
