using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Media;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.PathEditorTab.Views;

public partial class PathEditorView : PathEditorViewBase
{
    public static readonly StyledProperty<int> SceneWidthProperty =
        AvaloniaProperty.Register<PathEditorView, int>(nameof(SceneWidth));

    public static readonly StyledProperty<double?> ViewportScaleProperty =
        AvaloniaProperty.Register<PathEditorView, double?>(nameof(ViewportScale));

    public double? ViewportScale
    {
        get => GetValue(ViewportScaleProperty);
        set => SetValue(ViewportScaleProperty, value);
    }

    public Action<Vector>? PanViewport { get; set; }
    private double _scale = 1;

    public PathEditorView()
    {
        InitializeComponent();
        InitializeEditor(canvas, view, delta => PanViewport?.Invoke(delta));
    }

    public void SetToolbarHost(Panel host) => Interaction.SetToolbarHost(host);

    public override void Refresh()
    {
        UpdateControlPointVisibility();
        view.SelectedOperations = GetSelectedAnchors().Select(t => t.DataContext).OfType<PathSegment>().ToArray();
        UpdateThumbPosition();
        view.InvalidateVisual();
    }

    public int SceneWidth
    {
        get => GetValue(SceneWidthProperty);
        set => SetValue(SceneWidthProperty, value);
    }

    public override double Scale => _scale;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SceneWidthProperty || change.Property == BoundsProperty || change.Property == ViewportScaleProperty)
        {
            if (ViewportScale is { } scale)
            {
                SetAndRaise(ScaleProperty, ref _scale, scale);
            }
            else if (SceneWidth != 0)
            {
                SetAndRaise(ScaleProperty, ref _scale, Bounds.Width / SceneWidth);
            }
            else
            {
                SetAndRaise(ScaleProperty, ref _scale, 1);
            }
        }
    }

    private void ToggleDragModeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is FARadioMenuFlyoutItem button && DataContext is PathEditorViewModel viewModel)
        {
            PathEditorHelper.ApplyDragMode(viewModel, button.Tag);
        }
    }
}
