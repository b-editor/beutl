using System.Numerics;

using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using BeUtl.Animation;
using BeUtl.Animation.Easings;
using BeUtl.ProjectSystem;
using BeUtl.ViewModels;

using Reactive.Bindings;

using static BeUtl.Views.Timeline;

namespace BeUtl.Views;

public sealed partial class AnimationTimeline : UserControl
{
    internal MouseFlags _seekbarMouseFlag = MouseFlags.MouseUp;
    private TimeSpan _clickedFrame;
    internal TimeSpan _pointerFrame;
    private AnimationTimelineViewModel? _viewModel;
    private IDisposable? _disposable0;
    private bool _isFirst = true;

    public AnimationTimeline()
    {
        Resources["AnimationToViewModelConverter"] =
            new FuncValueConverter<IAnimationSpan, object?>(a =>
                a == null
                    ? null
                    : new ViewModels.AnimationEditors.AnimationEditorViewModel(
                        animationSpan: a,
                        property: ViewModel.WrappedProperty,
                        optionsProvider: ViewModel.OptionsProvider));

        InitializeComponent();
        ContentScroll.ScrollChanged += ContentScroll_ScrollChanged;
        ContentScroll.AddHandler(PointerWheelChangedEvent, ContentScroll_PointerWheelChanged, RoutingStrategies.Tunnel);
        ScaleScroll.AddHandler(PointerWheelChangedEvent, ContentScroll_PointerWheelChanged, RoutingStrategies.Tunnel);
        TimelinePanel.AddHandler(DragDrop.DragOverEvent, TimelinePanel_DragOver);
        TimelinePanel.AddHandler(DragDrop.DropEvent, TimelinePanel_Drop);
    }

    internal AnimationTimelineViewModel ViewModel => _viewModel!;

    // DataContextが変更された
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        _disposable0?.Dispose();
        if (DataContext is AnimationTimelineViewModel vm)
        {
            _viewModel = vm;

            _disposable0 = vm.OptionsProvider.Options.Subscribe(options =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Vector2 offset = options.Offset;
                    ScaleScroll.Offset = new(offset.X, 0);
                    ContentScroll.Offset = new(offset.X, offset.Y);
                });
            });
        }
    }

    // ContentScrollがスクロールされた
    private void ContentScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        IReactiveProperty<TimelineOptions>? options = ViewModel.OptionsProvider.Options;
        if (_isFirst)
        {
            ContentScroll.Offset = new(options.Value.Offset.X, options.Value.Offset.Y);

            _isFirst = false;
        }

        options.Value = options.Value with
        {
            Offset = new Vector2((float)ContentScroll.Offset.X, (float)ContentScroll.Offset.Y)
        };

        ScaleScroll.Offset = new(ContentScroll.Offset.X, 0);
    }

    // マウスホイールが動いた
    private void ContentScroll_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        IReactiveProperty<TimelineOptions>? options = ViewModel.OptionsProvider.Options;
        Avalonia.Vector offset = ContentScroll.Offset;

        if (e.KeyModifiers == KeyModifiers.Control)
        {
            // 目盛りのスケールを変更
            float scale = options.Value.Scale;
            var ts = offset.X.ToTimeSpan(scale);
            float deltaScale = (float)(e.Delta.Y / 120) * 10 * scale;
            options.Value = options.Value with
            {
                Scale = deltaScale + scale,
            };

            offset = offset.WithX(ts.ToPixel(options.Value.Scale));
        }
        else if (e.KeyModifiers == KeyModifiers.Shift)
        {
            // オフセット(Y) をスクロール
            offset = offset.WithY(offset.Y - (e.Delta.Y * 50));
        }
        else
        {
            // オフセット(X) をスクロール
            offset = offset.WithX(offset.X - (e.Delta.Y * 50));
        }

        ContentScroll.Offset = offset;
        e.Handled = true;
    }

    // ポインター移動
    private void TimelinePanel_PointerMoved(object? sender, PointerEventArgs e)
    {
        PointerPoint pointerPt = e.GetCurrentPoint(TimelinePanel);
        _pointerFrame = pointerPt.Position.X.ToTimeSpan(ViewModel.OptionsProvider.Options.Value.Scale)
            .RoundToRate(ViewModel.Scene.Parent is Project proj ? proj.GetFrameRate() : 30);

        if (_seekbarMouseFlag == MouseFlags.MouseDown)
        {
            ViewModel.Scene.CurrentFrame = _pointerFrame;
        }
    }

    // ポインターが放された
    private void TimelinePanel_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        PointerPoint pointerPt = e.GetCurrentPoint(TimelinePanel);

        if (pointerPt.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            _seekbarMouseFlag = MouseFlags.MouseUp;
        }
    }

    // ポインターが押された
    private void TimelinePanel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint pointerPt = e.GetCurrentPoint(TimelinePanel);
        _clickedFrame = pointerPt.Position.X.ToTimeSpan(ViewModel.OptionsProvider.Options.Value.Scale)
            .RoundToRate(ViewModel.Scene.Parent is Project proj ? proj.GetFrameRate() : 30);

        if (pointerPt.Properties.IsLeftButtonPressed)
        {
            _seekbarMouseFlag = MouseFlags.MouseDown;
            ViewModel.Scene.CurrentFrame = _clickedFrame;
        }
    }

    // ポインターが離れた
    private void TimelinePanel_PointerExited(object? sender, PointerEventArgs e)
    {
        _seekbarMouseFlag = MouseFlags.MouseUp;
    }

    private void TimelinePanel_Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("Easing") is Easing easing)
        {
            ViewModel.AddAnimation(easing);
            e.Handled = true;
        }
    }

    private void TimelinePanel_DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("Easing"))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }
}
