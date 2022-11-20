using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;

using Beutl.ViewModels;

namespace Beutl.Views;

public partial class InlineAnimationLayer : UserControl
{
    private readonly CrossFade _transition = new(TimeSpan.FromMilliseconds(250));
    private CancellationTokenSource? _lastTransitionCts;
    private IDisposable? _disposable1;
    private IDisposable? _disposable2;

    public InlineAnimationLayer()
    {
        InitializeComponent();
        this.SubscribeDataContextChange<InlineAnimationLayerViewModel>(
            OnDataContextAttached,
            OnDataContextDetached);

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _lastTransitionCts?.Cancel();
    }

    private void OnDataContextDetached(InlineAnimationLayerViewModel obj)
    {
        obj.AnimationRequested = null;
        _disposable1?.Dispose();
        _disposable1 = null;
        _disposable2?.Dispose();
        _disposable2 = null;
    }

    private void OnDataContextAttached(InlineAnimationLayerViewModel obj)
    {
        _disposable1 = obj.IsExpanded.Subscribe(OnIsExpandedChanged);

        obj.AnimationRequested = async (margin, token) =>
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var animation = new Avalonia.Animation.Animation
                {
                    Easing = new SplineEasing(0.1, 0.9, 0.2, 1.0),
                    Duration = TimeSpan.FromSeconds(0.67),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame()
                        {
                            Cue = new Cue(0),
                            Setters =
                            {
                                new Setter(MarginProperty, Margin)
                            }
                        },
                        new KeyFrame()
                        {
                            Cue = new Cue(1),
                            Setters =
                            {
                                new Setter(MarginProperty, margin)
                            }
                        }
                    }
                };

                await animation.RunAsync(this, null, token);
            });
        };
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is InlineAnimationLayerViewModel viewModel
            && viewModel.IsExpanded.Value
            && e.Data.Get("Easing") is Animation.Easings.Easing easing)
        {
            viewModel.AddAnimation(easing);
            e.Handled = true;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("Easing"))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void OnIsExpandedChanged(bool obj)
    {
        if (DataContext is InlineAnimationLayerViewModel viewModel)
        {
            _lastTransitionCts?.Cancel();
            _lastTransitionCts = new CancellationTokenSource();
            CancellationToken localToken = _lastTransitionCts.Token;

            if (obj)
            {
                viewModel.Height = Helper.LayerHeight * 2;
            }
            else
            {
                viewModel.Height = Helper.LayerHeight;
            }

            await _transition.Start(null, this, localToken);
        }
    }
}
