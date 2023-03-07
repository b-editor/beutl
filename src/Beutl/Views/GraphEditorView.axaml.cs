using System.Numerics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.ProjectSystem;
using Beutl.Utilities;
using Beutl.ViewModels;

using Reactive.Bindings.Extensions;

namespace Beutl.Views;

public partial class GraphEditorView : UserControl
{
    private bool _pressed;
    private TimeSpan _pointerFrame;
    private CompositeDisposable _disposables = new(2);

    public GraphEditorView()
    {
        InitializeComponent();
        scale.PointerExited += OnContentPointerExited;
        scale.PointerMoved += OnContentPointerMoved;
        scale.PointerReleased += OnContentPointerReleased;
        scale.PointerPressed += OnContentPointerPressed;
        graphPanel.PointerExited += OnContentPointerExited;
        graphPanel.PointerMoved += OnContentPointerMoved;
        graphPanel.PointerReleased += OnContentPointerReleased;
        graphPanel.PointerPressed += OnContentPointerPressed;

        scale.AddHandler(PointerWheelChangedEvent, OnContentPointerWheelChanged, RoutingStrategies.Tunnel);
        graphPanel.AddHandler(PointerWheelChangedEvent, OnContentPointerWheelChanged, RoutingStrategies.Tunnel);

        this.SubscribeDataContextChange<GraphEditorViewModel>(
            OnDataContextAttached,
            OnDataContextDetached);
    }

    private void OnDataContextDetached(GraphEditorViewModel obj)
    {
        _disposables.Clear();
    }

    private void OnDataContextAttached(GraphEditorViewModel obj)
    {
        obj.Options
            .Select(options => options.Offset)
            .Subscribe(offset => scroll.Offset = new(offset.X, offset.Y))
            .DisposeWith(_disposables);

        obj.MinHeight
            .CombineLatest(scroll.GetObservable(BoundsProperty))
            .ObserveOnUIDispatcher()
            .Subscribe(v => graphPanel.Height = Math.Max(v.First, v.Second.Height))
            .DisposeWith(_disposables);
    }

    private static float CoerceScaleX(float value)
    {
        if (MathUtilities.AreClose(value, 1))
            value = 1F;
        else if (MathUtilities.AreClose(value, 2))
            value = 2F;
        else if (MathUtilities.AreClose(value, 0.75))
            value = 0.75F;
        else if (MathUtilities.AreClose(value, 0.50))
            value = 0.50F;
        else if (MathUtilities.AreClose(value, 0.25))
            value = 0.25F;

        return Math.Min(value, 2);
    }

    private void OnContentPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel)
        {
            Avalonia.Vector aOffset = scroll.Offset;
            float scale = viewModel.Options.Value.Scale;
            var offset = new Vector2((float)aOffset.X, (float)aOffset.Y);

            if (e.KeyModifiers == KeyModifiers.Control)
            {
                // 目盛りのスケールを変更
                float oldScale = viewModel.Options.Value.Scale;
                TimeSpan ts = offset.X.ToTimeSpanF(oldScale);
                float deltaScale = (float)(e.Delta.Y / 10) * oldScale;
                scale = CoerceScaleX(deltaScale + oldScale);

                offset.X = ts.ToPixelF(scale);
            }
            else if (e.KeyModifiers.HasAllFlags(KeyModifiers.Control | KeyModifiers.Shift))
            {
                double oldScale = viewModel.ScaleY.Value;
                double scaleY = oldScale + (e.Delta.Y / 100);
                scaleY = Math.Clamp(scaleY, 0.01, 8.75);

                //offset.Y *= scaleY;
                viewModel.ScaleY.Value = scaleY;
            }
            else if (e.KeyModifiers == KeyModifiers.Shift)
            {
                // オフセット(X) をスクロール
                offset.X -= (float)(e.Delta.X * 50);
            }
            else
            {
                // オフセット(Y) をスクロール
                offset.Y -= (float)(e.Delta.Y * 50);
            }

            viewModel.Options.Value = viewModel.Options.Value with
            {
                Scale = scale,
                Offset = offset
            };

            e.Handled = true;
        }
    }

    private void OnContentPointerExited(object? sender, PointerEventArgs e)
    {
        _pressed = false;
    }

    private void OnContentPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel)
        {
            PointerPoint pointerPt = e.GetCurrentPoint(graphPanel);

            _pointerFrame = pointerPt.Position.X.ToTimeSpan(viewModel.Options.Value.Scale)
                .RoundToRate(viewModel.Scene.Parent is Project proj ? proj.GetFrameRate() : 30);

            if (_pressed)
            {
                viewModel.Scene.CurrentFrame = _pointerFrame;
            }
        }
    }

    private void OnContentPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        PointerPoint pointerPt = e.GetCurrentPoint(graphPanel);

        if (pointerPt.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            _pressed = false;
        }
    }

    private void OnContentPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel)
        {
            PointerPoint pointerPt = e.GetCurrentPoint(graphPanel);

            if (pointerPt.Properties.IsLeftButtonPressed)
            {
                _pressed = true;

                viewModel.Scene.CurrentFrame = pointerPt.Position.X.ToTimeSpan(viewModel.Options.Value.Scale)
                    .RoundToRate(viewModel.Scene.Parent is Project proj ? proj.GetFrameRate() : 30);
            }
        }
    }

}
