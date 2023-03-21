using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Generators;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

using Beutl.Animation.Easings;
using Beutl.ProjectSystem;
using Beutl.ViewModels;

namespace Beutl.Views;

public partial class InlineAnimationLayer : UserControl
{
    public InlineAnimationLayer()
    {
        InitializeComponent();
        Height = Helper.LayerHeight;
        this.SubscribeDataContextChange<InlineAnimationLayerViewModel>(
            OnDataContextAttached,
            OnDataContextDetached);

        items.ItemContainerGenerator.Materialized += OnMaterialized;
        items.ItemContainerGenerator.Dematerialized += OnDematerialized;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrap);
    }

    private void OnDrap(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("Easing") is Easing easing
            && DataContext is InlineAnimationLayerViewModel { Timeline: { Options.Value.Scale: { } scale, Scene:{ }scene } } viewModel)
        {
            Project? proj = scene.FindHierarchicalParent<Project>();
            int rate = proj?.GetFrameRate() ?? 30;

            TimeSpan time = e.GetPosition(this).X.ToTimeSpan(scale).RoundToRate(rate);
            viewModel.InsertKeyFrame(easing, time);
            e.Handled = true;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("Easing"))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnMaterialized(object? sender, ItemContainerEventArgs e)
    {
        foreach (ItemContainerInfo item in e.Containers)
        {
            Interaction.GetBehaviors(item.ContainerControl).Add(new _DragBehavior());
        }
    }

    private void OnDematerialized(object? sender, ItemContainerEventArgs e)
    {
        foreach (ItemContainerInfo item in e.Containers)
        {
            Interaction.GetBehaviors(item.ContainerControl).Clear();
        }
    }

    private void OnDataContextDetached(InlineAnimationLayerViewModel obj)
    {
        obj.AnimationRequested = null;
    }

    private void OnDataContextAttached(InlineAnimationLayerViewModel obj)
    {
        obj.AnimationRequested = async (margin, token) =>
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var animation = new Avalonia.Animation.Animation
                {
                    Easing = new Avalonia.Animation.Easings.SplineEasing(0.1, 0.9, 0.2, 1.0),
                    Duration = TimeSpan.FromSeconds(0.25),
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

    private sealed class _DragBehavior : Behavior<IControl>
    {
        private bool _pressed;
        private Point _start;

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject is InputElement ie)
            {
                ie.PointerPressed += OnPointerPressed;
                ie.PointerReleased += OnPointerReleased;
                ie.PointerMoved += OnPointerMoved;
            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (AssociatedObject is InputElement ie)
            {
                ie.PointerPressed -= OnPointerPressed;
                ie.PointerReleased -= OnPointerReleased;
                ie.PointerMoved -= OnPointerMoved;
            }
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (AssociatedObject is { DataContext: InlineKeyFrameViewModel viewModel }
                && _pressed)
            {
                Point position = e.GetPosition(AssociatedObject);
                Point delta = position - _start;
                _start = position;

                viewModel.Left.Value += delta.X;
                e.Handled = true;
            }
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (AssociatedObject is { DataContext: InlineKeyFrameViewModel viewModel }
                && _pressed)
            {
                viewModel.UpdateKeyTime();
                _pressed = false;
                e.Handled = true;
            }
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject != null)
            {
                PointerPoint point = e.GetCurrentPoint(AssociatedObject);

                if (point.Properties.IsLeftButtonPressed)
                {
                    _pressed = true;
                    _start = point.Position;
                    e.Handled = true;
                }
            }
        }
    }
}
