using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Helpers;
using Beutl.Services;
using Beutl.ViewModels;
using KeyFrame = Avalonia.Animation.KeyFrame;

namespace Beutl.Views;

public partial class InlineAnimationLayer : UserControl
{
    private TimeSpan _pointerPosition;

    public InlineAnimationLayer()
    {
        InitializeComponent();
        Height = FrameNumberHelper.LayerHeight;
        this.SubscribeDataContextChange<InlineAnimationLayerViewModel>(
            OnDataContextAttached,
            OnDataContextDetached);

        items.ContainerPrepared += OnMaterialized;
        items.ContainerClearing += OnDematerialized;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrap);
    }

    private void OnDrap(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(KnownLibraryItemFormats.Easing)) return;
        if (e.Data.Get(KnownLibraryItemFormats.Easing) is not Easing easing) return;
        if (DataContext is not InlineAnimationLayerViewModel viewModel) return;

        float scale = viewModel.Timeline.Options.Value.Scale;
        TimeSpan time = e.GetPosition(this).X.ToTimeSpan(scale);
        viewModel.DropEasing(easing, time);
        e.Handled = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(KnownLibraryItemFormats.Easing)) return;

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnMaterialized(object? sender, ContainerPreparedEventArgs e)
    {
        Interaction.GetBehaviors(e.Container).Add(new _DragBehavior());
    }

    private void OnDematerialized(object? sender, ContainerClearingEventArgs e)
    {
        Interaction.GetBehaviors(e.Container).Clear();
    }

    private void OnDataContextDetached(InlineAnimationLayerViewModel obj)
    {
        obj.AnimationRequested = null;
    }

    private void OnDataContextAttached(InlineAnimationLayerViewModel obj)
    {
        obj.AnimationRequested = async (margin, leftMargin, token) =>
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var animation1 = new Avalonia.Animation.Animation
                {
                    Easing = new Avalonia.Animation.Easings.SplineEasing(0.1, 0.9, 0.2, 1.0),
                    Duration = TimeSpan.FromSeconds(0.25),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame() { Cue = new Cue(0), Setters = { new Setter(MarginProperty, Margin) } },
                        new KeyFrame() { Cue = new Cue(1), Setters = { new Setter(MarginProperty, margin) } }
                    }
                };
                var animation2 = new Avalonia.Animation.Animation
                {
                    Easing = new Avalonia.Animation.Easings.SplineEasing(0.1, 0.9, 0.2, 1.0),
                    Duration = TimeSpan.FromSeconds(0.25),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame()
                        {
                            Cue = new Cue(0), Setters = { new Setter(MarginProperty, items.Margin) }
                        },
                        new KeyFrame()
                        {
                            Cue = new Cue(1), Setters = { new Setter(MarginProperty, leftMargin) }
                        }
                    }
                };

                Task task1 = animation1.RunAsync(this, token);
                Task task2 = animation2.RunAsync(items, token);
                await Task.WhenAll(task1, task2);
            });
        };
    }

    private async void CopyKeyframeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: InlineKeyFrameViewModel itemViewModel }) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null) return;
        var dataObject = new DataObject();
        ObjectRegenerator.Regenerate(itemViewModel.Model, out string json);
        dataObject.Set(DataFormats.Text, json);
        dataObject.Set(nameof(IKeyFrame), json);

        await topLevel.Clipboard.SetDataObjectAsync(dataObject);
    }

    private async void PasteKeyframeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InlineAnimationLayerViewModel viewModel) return;
        if (sender is not MenuItem { DataContext: InlineKeyFrameViewModel itemViewModel }) return;

        IClipboard? clipboard = App.GetClipboard();
        if (clipboard == null) return;

        string[] formats = await clipboard.GetFormatsAsync();

        if (formats.Contains(nameof(IKeyFrame)))
        {
            var json = await clipboard.GetDataAsync(nameof(IKeyFrame)) as byte[];
            var jsonNode = JsonNode.Parse(json!);
            if (jsonNode is not JsonObject jsonObj)
            {
                NotificationService.ShowWarning("", "Invalid keyframe data format.");
                return;
            }

            if (jsonObj.TryGetDiscriminator(out Type? type)
                && Activator.CreateInstance(type) is IKeyFrame keyframe)
            {
                CoreSerializerHelper.PopulateFromJsonObject(keyframe, type, jsonObj);
                keyframe.KeyTime = itemViewModel.Model.KeyTime;
                // 現在のキーフレームを新しいものに置き換える

                viewModel.ReplaceKeyFrame(itemViewModel.Model, keyframe);
            }
        }
        else
        {
            NotificationService.ShowWarning("", "Invalid keyframe data format.");
        }
    }

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InlineAnimationLayerViewModel viewModel
            && sender is MenuItem { DataContext: InlineKeyFrameViewModel itemViewModel })
        {
            viewModel.RemoveKeyFrame(itemViewModel.Model);
        }
    }

    private async void CopyAllKeyframeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InlineAnimationLayerViewModel viewModel) return;

        IClipboard? clipboard = App.GetClipboard();
        if (clipboard == null) return;

        string json = CoreSerializerHelper.SerializeToJsonString((IKeyFrameAnimation)viewModel.Property.Animation!, typeof(IKeyFrameAnimation));

        var data = new DataObject();
        data.Set(DataFormats.Text, json);
        data.Set(nameof(IKeyFrameAnimation), json);

        await clipboard.SetDataObjectAsync(data);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if(DataContext is not InlineAnimationLayerViewModel viewModel) return;
        Point point = e.GetPosition(this);
        float scale = viewModel.Timeline.Options.Value.Scale;
        _pointerPosition = point.X.ToTimeSpan(scale);
    }

    private async void PasteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InlineAnimationLayerViewModel viewModel) return;

        IClipboard? clipboard = App.GetClipboard();
        if (clipboard == null) return;

        string[] formats = await clipboard.GetFormatsAsync();

        if (formats.Contains(nameof(IKeyFrame)))
        {
            var json = await clipboard.GetDataAsync(nameof(IKeyFrame)) as byte[];
            var jsonNode = JsonNode.Parse(json!);
            if (jsonNode is not JsonObject jsonObj)
            {
                NotificationService.ShowWarning("", "Invalid keyframe data format.");
                return;
            }

            if (jsonObj.TryGetDiscriminator(out Type? type)
                && Activator.CreateInstance(type) is IKeyFrame keyframe)
            {
                CoreSerializerHelper.PopulateFromJsonObject(keyframe, type, jsonObj);
                keyframe.KeyTime = _pointerPosition;
                // 現在のキーフレームを新しいものに置き換える

                viewModel.InsertKeyFrame(keyframe);
            }
        }
        else if (formats.Contains(nameof(IKeyFrameAnimation)))
        {
            byte[]? json = await clipboard.GetDataAsync(nameof(IKeyFrameAnimation)) as byte[];
            viewModel.PasteAnimation(System.Text.Encoding.UTF8.GetString(json!));
        }
        else
        {
            NotificationService.ShowWarning("", "Invalid keyframe data format.");
        }
    }

    private sealed class _DragBehavior : Behavior<Control>
    {
        private bool _pressed;
        private Point _start;
        private InlineKeyFrameViewModel[]? _items;

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject is not InputElement ie) return;

            ie.PointerPressed += OnPointerPressed;
            ie.PointerReleased += OnPointerReleased;
            ie.PointerMoved += OnPointerMoved;
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (AssociatedObject is not InputElement ie) return;

            ie.PointerPressed -= OnPointerPressed;
            ie.PointerReleased -= OnPointerReleased;
            ie.PointerMoved -= OnPointerMoved;
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (AssociatedObject is not { DataContext: InlineKeyFrameViewModel viewModel }) return;
            if (!_pressed) return;

            Point position = e.GetPosition(AssociatedObject);
            Point delta = position - _start;
            _start = position;

            viewModel.Left.Value += delta.X;
            e.Handled = true;

            if (_items == null) return;
            foreach (InlineKeyFrameViewModel item in _items)
            {
                item.Left.Value += delta.X;
            }
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (AssociatedObject is not { DataContext: InlineKeyFrameViewModel viewModel }) return;
            if (!_pressed) return;

            if (_items != null)
            {
                _items.Select(i => i.CreateUpdateCommand())
                    .Append(viewModel.CreateUpdateCommand())
                    .ToArray()
                    .ToCommand([viewModel.Parent.Element.Model])
                    .DoAndRecord(viewModel.Timeline.EditorContext.CommandRecorder);
            }
            else
            {
                viewModel.UpdateKeyTime();
            }

            _items = null;
            _pressed = false;
            e.Handled = true;
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject is not { DataContext: InlineKeyFrameViewModel viewModel }) return;

            PointerPoint point = e.GetCurrentPoint(AssociatedObject);

            if (!point.Properties.IsLeftButtonPressed) return;

            _pressed = true;
            _start = point.Position;
            e.Handled = true;

            if (e.KeyModifiers == KeyModifiers.Shift)
            {
                _items = viewModel.Parent.Items.Where(i => i != viewModel).ToArray();
            }
        }
    }

    private void DeleteAnimationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InlineAnimationLayerViewModel viewModel
            && viewModel.Property.Animation is {}animation)
        {
            (viewModel.Timeline.EditorContext as ISupportCloseAnimation).Close(animation);
            viewModel.DeleteAnimation();
        }
    }
}
