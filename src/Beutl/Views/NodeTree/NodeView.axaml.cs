using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using Beutl.ViewModels.NodeTree;

namespace Beutl.Views.NodeTree;

public partial class NodeView : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));

    private static readonly ITransform s_rotate_180deg_transform;
    private CancellationTokenSource? _lastTransitionCts;

    private Point _start;
    private bool _captured;

    static NodeView()
    {
        TransformOperations.Builder builder = TransformOperations.CreateBuilder(1);
        builder.AppendRotate(Math.PI);

        s_rotate_180deg_transform = builder.Build();
    }

    public NodeView()
    {
        InitializeComponent();

        handle.PointerPressed += OnHandlePointerPressed;
        handle.PointerReleased += OnHandlePointerReleased;
        handle.PointerMoved += OnHandlePointerMoved;

        expandToggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(v =>
            {
                ExpandCollapseChevron.RenderTransform = v == true
                                                ? s_rotate_180deg_transform
                                                : TransformOperations.Identity;

                _lastTransitionCts?.Cancel();
                _lastTransitionCts = new CancellationTokenSource();
                CancellationToken localToken = _lastTransitionCts.Token;

                nodeContent.IsVisible = v == true;

                _ = s_transition.Start(null, this, localToken);
            });
    }

    private void OnHandlePointerMoved(object? sender, PointerEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (_captured
            && properties.IsLeftButtonPressed)
        {
            Point position = e.GetPosition(Parent);
            Point delta = position - _start;
            _start = position;
            double left = Canvas.GetLeft(this) + delta.X;
            double top = Canvas.GetTop(this) + delta.Y;
            Canvas.SetLeft(this, left);
            Canvas.SetTop(this, top);

            e.Handled = true;
        }
    }

    private void OnHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_captured)
        {
            _captured = false;
            e.Handled = true;

            if (Parent is Canvas canvas)
            {
                int minZindex = canvas.Children.Max(x => x.ZIndex);
                for (int i = 0; i < canvas.Children.Count; i++)
                {
                    IControl? item = canvas.Children[i];
                    item.ZIndex -= minZindex;
                }
            }

            if (DataContext is NodeViewModel viewModel)
            {
                viewModel.NotifyPositionChange(new Point(Canvas.GetLeft(this), Canvas.GetTop(this)));
            }
        }
    }

    private void OnHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsLeftButtonPressed)
        {
            _start = e.GetPosition(Parent);
            _captured = true;

            e.Handled = true;

            if (Parent is Canvas canvas)
            {
                ZIndex = canvas.Children.Max(x => x.ZIndex) + 1;
            }
        }
    }
}
