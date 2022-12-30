using Beutl.Media;
using Beutl.Styling;

namespace Beutl.Rendering;

public abstract class Renderable : Styleable, IRenderable, IAffectsRender
{
    public static readonly CoreProperty<bool> IsVisibleProperty;
    private bool _isVisible = true;

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    static Renderable()
    {
        IsVisibleProperty = ConfigureProperty<bool, Renderable>(nameof(IsVisible))
            .Accessor(o => o.IsVisible, (o, v) => o.IsVisible = v)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .DefaultValue(true)
            .Register();

        AffectsRender<Renderable>(IsVisibleProperty);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetAndRaise(IsVisibleProperty, ref _isVisible, value);
    }

    private void AffectsRender_Invalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(this, e);
    }

    protected static void AffectsRender<T>(params CoreProperty[] properties)
        where T : Renderable
    {
        foreach (CoreProperty item in properties)
        {
            item.Changed.Subscribe(e =>
            {
                if (e.Sender is T s)
                {
                    s.Invalidated?.Invoke(s, new RenderInvalidatedEventArgs(s, e.Property.Name));

                    if (e.OldValue is IAffectsRender oldAffectsRender)
                    {
                        oldAffectsRender.Invalidated -= s.AffectsRender_Invalidated;
                    }

                    if (e.NewValue is IAffectsRender newAffectsRender)
                    {
                        newAffectsRender.Invalidated += s.AffectsRender_Invalidated;
                    }
                }
            });
        }
    }

    public void Invalidate()
    {
        Invalidated?.Invoke(this, new RenderInvalidatedEventArgs(this));
    }

    public abstract void Render(IRenderer renderer);
}
