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
        IsEnabledProperty = ConfigureProperty<bool, StreamOperator>(nameof(IsEnabled))
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

        // Todo: Layer.OperatorsでInvalidatedを購読して、プレビューを更新するようにする。
        Layer? layer = this.FindLogicalParent<Layer>();
        Scene? scene = this.FindLogicalParent<Scene>();

        if (scene != null
            && layer != null
            && layer.IsEnabled
            && layer.Start <= scene.CurrentFrame
            && scene.CurrentFrame < layer.Start + layer.Length
            && scene?.Renderer is SceneRenderer { IsDisposed: false, IsRendering: false } renderer)
        {
            renderer.Invalidate(scene.CurrentFrame);
        }
    }
}
