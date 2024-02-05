using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

using Beutl.Commands;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Beutl.ViewModels.NodeTree;

using FluentAvalonia.UI.Controls;

using Reactive.Bindings.Extensions;

using Setter = Avalonia.Styling.Setter;

namespace Beutl.Views;

/*
 * 移動アニメーション中にUndoを行うと、
 * 表示される位置がUndo前になる。
 * 解決するには、オブジェクトがUndo/Redoしたかを追跡するAPIを追加して、
 * Undo/Redoがされた場合アニメーションをキャンセルする必要がある。
 */

public sealed partial class ElementView : UserControl
{
    private readonly CompositeDisposable _disposables = [];
    private Timeline? _timeline;
    private TimeSpan _pointerPosition;
    private static ColorPickerFlyout? s_colorPickerFlyout;
    private _ResizeBehavior? _resizeBehavior;

    public ElementView()
    {
        InitializeComponent();

        (border.ContextFlyout as FAMenuFlyout)!.Opening += OnContextFlyoutOpening;
        textBox.LostFocus += OnTextBoxLostFocus;
        this.SubscribeDataContextChange<ElementViewModel>(OnDataContextAttached, OnDataContextDetached);
    }

    private ElementViewModel ViewModel => (ElementViewModel)DataContext!;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is ElementViewModel viewModel)
        {
            if (e.Key == Key.F2)
            {
                Rename_Click(null, null!);
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.LeftCtrl)
            {
                _resizeBehavior?.OnLeftCtrlPressed(e);
                return;
            }

            // KeyBindingsは変更してはならない。
            foreach (KeyBinding binding in viewModel.KeyBindings)
            {
                if (e.Handled)
                    break;
                binding.TryHandle(e);
            }
        }
    }

    private void OnContextFlyoutOpening(object? sender, EventArgs e)
    {
        if (DataContext is ElementViewModel viewModel)
        {
            change2OriginalLength.IsEnabled = viewModel.HasOriginalLength();
            splitByCurrent.IsEnabled = viewModel.Model.Range.Contains(viewModel.Timeline.EditorContext.CurrentTime.Value);
        }
    }

    private void OnDataContextDetached(ElementViewModel obj)
    {
        obj.AnimationRequested = (_, _) => Task.CompletedTask;
        obj.GetClickedTime = null;
        _disposables.Clear();
    }

    private void OnDataContextAttached(ElementViewModel obj)
    {
        obj.AnimationRequested = async (args, token) =>
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var animation1 = new Avalonia.Animation.Animation
                {
                    Easing = new SplineEasing(0.1, 0.9, 0.2, 1.0),
                    Duration = TimeSpan.FromSeconds(0.25),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame()
                        {
                            Cue = new Cue(0),
                            Setters =
                            {
                                new Setter(MarginProperty, border.Margin),
                                new Setter(WidthProperty, border.Width),
                            }
                        },
                        new KeyFrame()
                        {
                            Cue = new Cue(1),
                            Setters =
                            {
                                new Setter(MarginProperty, args.BorderMargin),
                                new Setter(WidthProperty, args.Width)
                            }
                        }
                    }
                };
                var animation2 = new Avalonia.Animation.Animation
                {
                    Easing = new SplineEasing(0.1, 0.9, 0.2, 1.0),
                    Duration = TimeSpan.FromSeconds(0.25),
                    FillMode = FillMode.Forward,
                    Children =
                        {
                            new KeyFrame()
                            {
                                Cue = new Cue(0),
                                Setters =
                                {
                                    new Setter(MarginProperty, obj.Margin.Value)
                                }
                            },
                            new KeyFrame()
                            {
                                Cue = new Cue(1),
                                Setters =
                                {
                                    new Setter(MarginProperty, args.Margin)
                                }
                            }
                        }
                };

                Task task1 = animation1.RunAsync(border, token);
                Task task2 = animation2.RunAsync(this, token);
                await Task.WhenAll(task1, task2);
            });
        };
        obj.GetClickedTime = () => _pointerPosition;

        obj.Model.GetObservable(Element.IsEnabledProperty)
            .ObserveOnUIDispatcher()
            .Subscribe(b => border.Opacity = b ? 1 : 0.5)
            .DisposeWith(_disposables);

        obj.IsSelected
            .ObserveOnUIDispatcher()
            .Subscribe(v => ZIndex = v ? 5 : 0)
            .DisposeWith(_disposables);
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        _timeline = this.FindLogicalAncestorOfType<Timeline>();

        BehaviorCollection behaviors = Interaction.GetBehaviors(this);
        behaviors.Clear();
        behaviors.Add(new _SelectBehavior());
        behaviors.Add(_resizeBehavior = new _ResizeBehavior());
        behaviors.Add(new _MoveBehavior());
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        _timeline = null;
        BehaviorCollection behaviors = Interaction.GetBehaviors(this);
        behaviors.Clear();
        _resizeBehavior = null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point point = e.GetPosition(this);
        float scale = ViewModel.Timeline.Options.Value.Scale;
        _pointerPosition = point.X.ToTimeSpan(scale);
    }

    private void UseNodeClick(object? sender, RoutedEventArgs e)
    {
        Element model = ViewModel.Model;
        CommandRecorder recorder = ViewModel.Timeline.EditorContext.CommandRecorder;
        RecordableCommands.Edit(model, Element.UseNodeProperty, !model.UseNode)
            .WithStoables([model])
            .DoAndRecord(recorder);
    }

    private void OnTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        textBlock.IsVisible = true;
        textBox.IsVisible = false;
    }

    private void Rename_Click(object? sender, RoutedEventArgs e)
    {
        textBlock.IsVisible = false;
        textBox.IsVisible = true;
        textBox.SelectAll();
        textBox.Focus();
    }

    private void ChangeColor_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ElementViewModel viewModel)
        {
            // ContextMenuから開いているので、閉じるのを待つ
            s_colorPickerFlyout ??= new ColorPickerFlyout();
            s_colorPickerFlyout.ColorPicker.Color = viewModel.Color.Value;
            s_colorPickerFlyout.ColorPicker.IsAlphaEnabled = false;
            s_colorPickerFlyout.ColorPicker.UseColorPalette = true;
            s_colorPickerFlyout.ColorPicker.IsCompact = true;
            s_colorPickerFlyout.ColorPicker.IsMoreButtonVisible = true;
            s_colorPickerFlyout.Placement = PlacementMode.Top;

            if (this.TryFindResource("PaletteColors", out object? colors)
                && colors is IEnumerable<Color> tcolors)
            {
                s_colorPickerFlyout.ColorPicker.CustomPaletteColors = tcolors;
            }

            s_colorPickerFlyout.Confirmed += OnColorPickerFlyoutConfirmed;
            s_colorPickerFlyout.Closed += OnColorPickerFlyoutClosed;

            s_colorPickerFlyout.ShowAt(border);
        }
    }

    private void OnColorPickerFlyoutClosed(object? sender, EventArgs e)
    {
        s_colorPickerFlyout!.Confirmed -= OnColorPickerFlyoutConfirmed;
        s_colorPickerFlyout!.Closed -= OnColorPickerFlyoutClosed;
    }

    private void OnColorPickerFlyoutConfirmed(ColorPickerFlyout sender, EventArgs args)
    {
        if (DataContext is ElementViewModel viewModel)
        {
            viewModel.Color.Value = sender.ColorPicker.Color;
        }
    }

    private void OpenNodeTree_Click(object? sender, RoutedEventArgs e)
    {
        Element model = ViewModel.Model;
        EditViewModel context = ViewModel.Timeline.EditorContext;
        NodeTreeTabViewModel? nodeTree = context.FindToolTab<NodeTreeTabViewModel>(
            v => v.Element.Value == model || v.Element.Value == null);
        nodeTree ??= new NodeTreeTabViewModel(context);
        nodeTree.Element.Value = model;

        context.OpenToolTab(nodeTree);
    }

    private TimeSpan RoundStartTime(TimeSpan time, float scale, bool flag)
    {
        Element model = ViewModel.Model;

        if (!flag)
        {
            foreach (Element item in ViewModel.Scene.Children.GetMarshal().Value)
            {
                if (item != model)
                {
                    const double ThreadholdPixel = 10;
                    TimeSpan threadhold = ThreadholdPixel.ToTimeSpan(scale);
                    TimeSpan start = item.Start;
                    TimeSpan end = start + item.Length;
                    var startRange = new Media.TimeRange(start - threadhold, threadhold);
                    var endRange = new Media.TimeRange(end - threadhold, threadhold);

                    if (endRange.Contains(time))
                    {
                        return end;
                    }
                    else if (startRange.Contains(time))
                    {
                        return start;
                    }
                }
            }
        }

        return time;
    }

    private sealed class _ResizeBehavior : Behavior<ElementView>
    {
        private Element? _before;
        private Element? _after;
        private bool _pressed;
        private TimeSpan _recordedEndTime;
        private AlignmentX _resizeType;

        public void OnLeftCtrlPressed(KeyEventArgs e)
        {
            if (AssociatedObject is { } view && !_pressed)
            {
                view.Cursor = null;
                _resizeType = AlignmentX.Center;
                e.Handled = true;
            }
        }

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject != null)
            {
                AssociatedObject.AddHandler(PointerMovedEvent, OnPointerMoved);
                AssociatedObject.border.AddHandler(PointerPressedEvent, OnBorderPointerPressed);
                AssociatedObject.border.AddHandler(PointerReleasedEvent, OnBorderPointerReleased);
                AssociatedObject.border.AddHandler(PointerMovedEvent, OnBorderPointerMoved);
            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (AssociatedObject != null)
            {
                AssociatedObject.RemoveHandler(PointerMovedEvent, OnPointerMoved);
                AssociatedObject.border.RemoveHandler(PointerPressedEvent, OnBorderPointerPressed);
                AssociatedObject.border.RemoveHandler(PointerMovedEvent, OnBorderPointerMoved);
            }
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (AssociatedObject is { ViewModel: { } viewModel } view)
            {
                Point point = e.GetPosition(view);
                float scale = viewModel.Timeline.Options.Value.Scale;
                TimeSpan pointerFrame = point.X.ToTimeSpan(scale);

                if (view._timeline is { } timeline && _pressed)
                {
                    pointerFrame = view.RoundStartTime(pointerFrame, scale, e.KeyModifiers.HasFlag(KeyModifiers.Alt));
                    point = point.WithX(pointerFrame.ToPixel(scale));
                    int rate = viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
                    double minWidth = TimeSpan.FromSeconds(1d / rate).ToPixel(scale);

                    if (view.Cursor != Cursors.Arrow && view.Cursor is { })
                    {
                        double left = viewModel.BorderMargin.Value.Left;

                        if (_resizeType == AlignmentX.Right)
                        {
                            // 右
                            double x = _after == null ? point.X : Math.Min(_after.Start.ToPixel(scale), point.X);
                            viewModel.Width.Value = Math.Max(x - left, minWidth);
                        }
                        else if (_resizeType == AlignmentX.Left && pointerFrame >= TimeSpan.Zero)
                        {
                            // 左
                            double x = _before == null ? point.X : Math.Max(_before.Range.End.ToPixel(scale), point.X);

                            double endPos = _recordedEndTime.ToPixel(scale);

                            double newWidth = endPos - x;
                            if (minWidth < newWidth)
                            {
                                viewModel.Width.Value = newWidth;
                                viewModel.BorderMargin.Value = new Thickness(x, 0, 0, 0);
                            }
                            else
                            {
                                viewModel.Width.Value = minWidth;
                                viewModel.BorderMargin.Value = new Thickness(endPos - minWidth, 0, 0, 0);
                            }
                        }

                        e.Handled = true;
                    }
                }
            }
        }

        private void OnBorderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject is { _timeline: { }, ViewModel: { } viewModel } view)
            {
                PointerPoint point = e.GetCurrentPoint(view.border);
                if (point.Properties.IsLeftButtonPressed && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Alt
                    && view.Cursor != Cursors.Arrow && view.Cursor is { })
                {
                    _before = viewModel.Model.GetBefore(viewModel.Model.ZIndex, viewModel.Model.Start);
                    _after = viewModel.Model.GetAfter(viewModel.Model.ZIndex, viewModel.Model.Range.End);
                    _recordedEndTime = viewModel.Model.Range.End;
                    _pressed = true;

                    e.Handled = true;
                }
            }
        }

        private async void OnBorderPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_pressed)
            {
                _before = null;
                _after = null;
                _pressed = false;

                if (AssociatedObject is { ViewModel: { } viewModel })
                {
                    await viewModel.SubmitViewModelChanges();
                    e.Handled = true;
                }
            }
        }

        private void OnBorderPointerMoved(object? sender, PointerEventArgs e)
        {
            if (AssociatedObject is { border: { } border, ViewModel: { } viewModel } view)
            {
                if (e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Alt))
                {
                    view.Cursor = null;
                    _resizeType = AlignmentX.Center;
                }
                else if (!_pressed)
                {
                    float scale = viewModel.Timeline.Options.Value.Scale;
                    int rate = viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
                    double minWidth = TimeSpan.FromSeconds(1d / rate).ToPixel(scale);

                    Point point = e.GetPosition(border);
                    double horizon = point.X;

                    double handleWidth = 10;
                    if (border.Width <= minWidth)
                    {
                        handleWidth = minWidth / 2;
                    }

                    // 左右 10px内 なら左右矢印
                    if (horizon < handleWidth)
                    {
                        view.Cursor = Cursors.SizeWestEast;
                        _resizeType = AlignmentX.Left;
                    }
                    else if (horizon > border.Bounds.Width - handleWidth)
                    {
                        view.Cursor = Cursors.SizeWestEast;
                        _resizeType = AlignmentX.Right;
                    }
                    else
                    {
                        view.Cursor = null;
                        _resizeType = AlignmentX.Center;
                    }
                }
            }
        }
    }

    private sealed class _MoveBehavior : Behavior<ElementView>
    {
        private bool _pressed;
        private Point _start;

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject != null)
            {
                AssociatedObject.AddHandler(PointerMovedEvent, OnPointerMoved);
                AssociatedObject.border.AddHandler(PointerPressedEvent, OnBorderPointerPressed);
                AssociatedObject.border.AddHandler(PointerReleasedEvent, OnBorderPointerReleased);
            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (AssociatedObject != null)
            {
                AssociatedObject.RemoveHandler(PointerMovedEvent, OnPointerMoved);
                AssociatedObject.border.RemoveHandler(PointerPressedEvent, OnBorderPointerPressed);
                AssociatedObject.border.RemoveHandler(PointerReleasedEvent, OnBorderPointerReleased);
            }
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (AssociatedObject is { ViewModel: { } viewModel } view
                && view._timeline is { } timeline && _pressed)
            {
                Point point = e.GetPosition(view);
                float scale = viewModel.Timeline.Options.Value.Scale;
                TimeSpan pointerFrame = point.X.ToTimeSpan(scale);

                pointerFrame = view.RoundStartTime(pointerFrame, scale, e.KeyModifiers.HasFlag(KeyModifiers.Alt));

                TimeSpan newframe = pointerFrame - _start.X.ToTimeSpan(scale);

                newframe = TimeSpan.FromTicks(Math.Max(newframe.Ticks, TimeSpan.Zero.Ticks));

                var newTop = Math.Max(e.GetPosition(timeline.TimelinePanel).Y - _start.Y, 0);
                var newLeft = newframe.ToPixel(scale);
                var deltaTop = newTop - viewModel.Margin.Value.Top;
                var deltaLeft = newLeft - viewModel.BorderMargin.Value.Left;

                viewModel.Margin.Value = new(0, newTop, 0, 0);
                viewModel.BorderMargin.Value = new Thickness(newLeft, 0, 0, 0);

                foreach (ElementViewModel item in viewModel.Timeline.GetSelected(viewModel))
                {
                    item.Margin.Value = new(0, item.Margin.Value.Top + deltaTop, 0, 0);
                    item.BorderMargin.Value = new(item.BorderMargin.Value.Left + deltaLeft, 0, 0, 0);
                }

                e.Handled = true;
            }
        }

        private void OnBorderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject is { _timeline: { }, border: { } border } view)
            {
                PointerPoint point = e.GetCurrentPoint(view.border);
                if (point.Properties.IsLeftButtonPressed
                    && (view.Cursor == Cursors.Arrow || view.Cursor == null))
                {
                    _pressed = true;
                    _start = point.Position;
                    e.Handled = true;
                }
            }
        }

        private async void OnBorderPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_pressed)
            {
                _pressed = false;

                if (AssociatedObject is { ViewModel: { } viewModel })
                {
                    CommandRecorder recorder = viewModel.Timeline.EditorContext.CommandRecorder;
                    e.Handled = true;
                    var elems = new List<Element>() { viewModel.Model };
                    elems.AddRange(viewModel.Timeline.GetSelected(viewModel).Select(x => x.Model));

                    if (elems.Count == 1)
                    {
                        await viewModel.SubmitViewModelChanges();
                    }
                    else
                    {
                        var animations = viewModel.Timeline.GetSelected(viewModel).Append(viewModel)
                            .Select(x => (ViewModel: x, Context: x.PrepareAnimation()))
                            .ToArray();

                        float scale = viewModel.Timeline.Options.Value.Scale;
                        int rate = viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
                        TimeSpan newStart = viewModel.BorderMargin.Value.Left.ToTimeSpan(scale).RoundToRate(rate);
                        TimeSpan deltaStart = newStart - viewModel.Model.Start;
                        int newIndex = viewModel.Timeline.ToLayerNumber(viewModel.Margin.Value);
                        int deltaIndex = newIndex - viewModel.Model.ZIndex;

                        viewModel.Scene.MoveChildren(deltaIndex, deltaStart, [.. elems])
                            .DoAndRecord(recorder);

                        foreach (var (item, context) in animations)
                        {
                            _ = item.AnimationRequest(context);
                        }
                    }
                }
            }
        }
    }

    private sealed class _SelectBehavior : Behavior<ElementView>
    {
        private bool _pressedWithModifier;
        private Thickness _snapshot;

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject != null)
            {
                AssociatedObject.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
                AssociatedObject.border.AddHandler(PointerPressedEvent, OnBorderPointerPressed);
                AssociatedObject.border.AddHandler(PointerReleasedEvent, OnBorderPointerReleased);
            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (AssociatedObject != null)
            {
                AssociatedObject.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
                AssociatedObject.border.RemoveHandler(PointerPressedEvent, OnBorderPointerPressed);
                AssociatedObject.border.RemoveHandler(PointerReleasedEvent, OnBorderPointerReleased);
            }
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject is { } obj)
            {
                if (!obj.textBox.IsFocused)
                {
                    obj.Focus();
                }
            }
        }

        private void OnBorderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject is { _timeline: { } } obj)
            {
                PointerPoint point = e.GetCurrentPoint(obj.border);
                if (point.Properties.IsLeftButtonPressed)
                {
                    if (e.ClickCount == 2)
                    {
                        obj.textBlock.IsVisible = false;
                        obj.textBox.IsVisible = true;
                        obj.textBox.SelectAll();
                        obj.textBox.Focus();
                    }
                    else
                    {
                        if (e.KeyModifiers is KeyModifiers.None or KeyModifiers.Alt)
                        {
                            EditViewModel editorContext = obj._timeline.ViewModel.EditorContext;
                            editorContext.SelectedObject.Value = obj.ViewModel.Model;
                            obj.ViewModel.IsSelected.Value = true;
                        }
                        else
                        {
                            Thickness margin = obj.ViewModel.Margin.Value;
                            Thickness borderMargin = obj.ViewModel.BorderMargin.Value;
                            _snapshot = new(borderMargin.Left, margin.Top, 0, 0);
                            _pressedWithModifier = true;
                        }

                        obj.border.Opacity = 0.8;
                    }
                }
            }
        }

        private void OnBorderPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (AssociatedObject is { _timeline: { } } obj)
            {
                if (_pressedWithModifier)
                {
                    Thickness margin = obj.ViewModel.Margin.Value;
                    Thickness borderMargin = obj.ViewModel.BorderMargin.Value;
                    if (borderMargin.Left == _snapshot.Left
                        && margin.Top == _snapshot.Top)
                    {
                        obj.ViewModel.IsSelected.Value = !obj.ViewModel.IsSelected.Value;
                    }

                    _pressedWithModifier = false;
                }

                obj.border.Opacity = 1;
            }
        }
    }
}
