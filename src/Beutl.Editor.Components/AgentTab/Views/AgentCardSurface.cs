using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Beutl.Editor.Components.AgentTab.Views;

/// <summary>
/// Paints the dock surface behind a card that floats over the scrolling timeline. The card
/// brush is translucent, so without this the timeline text would show through. The surface is
/// drawn in the child's bounds (excluding its margin) with the child's corner radius, so the
/// card looks the same as when it sits directly on the panel but is opaque.
/// </summary>
internal sealed class AgentCardSurface : Decorator
{
    /// <summary>Opaque brush at the bottom of the stack (the workbench behind the dock panel).</summary>
    public static readonly StyledProperty<IBrush?> BaseBrushProperty =
        AvaloniaProperty.Register<AgentCardSurface, IBrush?>(nameof(BaseBrush));

    /// <summary>The dock panel brush, which is itself translucent in the light theme.</summary>
    public static readonly StyledProperty<IBrush?> SurfaceBrushProperty =
        AvaloniaProperty.Register<AgentCardSurface, IBrush?>(nameof(SurfaceBrush));

    static AgentCardSurface() => AffectsRender<AgentCardSurface>(BaseBrushProperty, SurfaceBrushProperty);

    public IBrush? BaseBrush { get => GetValue(BaseBrushProperty); set => SetValue(BaseBrushProperty, value); }
    public IBrush? SurfaceBrush { get => GetValue(SurfaceBrushProperty); set => SetValue(SurfaceBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Child is not { IsVisible: true } child)
            return;

        var corners = child is Border border ? border.CornerRadius : default;
        var rect = new RoundedRect(child.Bounds, corners);
        if (BaseBrush is { } baseBrush)
            context.DrawRectangle(baseBrush, null, rect);
        if (SurfaceBrush is { } surface)
            context.DrawRectangle(surface, null, rect);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        // The surface follows the child's bounds, which can change without this control resizing.
        InvalidateVisual();
        return size;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ChildProperty)
            return;

        if (change.OldValue is Border oldBorder)
            oldBorder.PropertyChanged -= OnChildPropertyChanged;
        if (change.NewValue is Border newBorder)
            newBorder.PropertyChanged += OnChildPropertyChanged;
    }

    private void OnChildPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Border.CornerRadiusProperty || e.Property == IsVisibleProperty)
            InvalidateVisual();
    }
}
