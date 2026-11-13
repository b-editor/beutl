using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class LayerHeader : UserControl
{
    public static readonly DirectProperty<LayerHeader, double> PositionYProperty
        = AvaloniaProperty.RegisterDirect<LayerHeader, double>(
            nameof(PositionY),
            o => o.PositionY,
            (o, v) => o.PositionY = v);

    private bool _pressed;
    private TimelineTabView? _timeline;
    private Point _startRel;
    private Point _start;
    private Element[] _elements = [];
    private int _newLayer;
    private double _positionY;
    private DragSession? _drag;

    public LayerHeader()
    {
        InitializeComponent();
        // A single-line text box marks Enter handled, so the name box takes it on the way down.
        NameTextBox.AddHandler(KeyDownEvent, NameTextBox_KeyDown, RoutingStrategies.Tunnel);
    }

    public double PositionY
    {
        get => _positionY;
        set
        {
            if (SetAndRaise(PositionYProperty, ref _positionY, value))
            {
                OnPositionYChanged();
            }
        }
    }

    private LayerHeaderViewModel ViewModel => (LayerHeaderViewModel)DataContext!;

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        _timeline = null;
    }

    protected override void OnDetachedFromLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        _timeline = null;
    }

    private TimelineTabView? GetOrFindTimeline()
    {
        return _timeline ??= this.FindLogicalAncestorOfType<TimelineTabView>();
    }

    private void Border_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed && GetOrFindTimeline() is { } timeline)
        {
            LayerHeaderViewModel vm = ViewModel;
            if (_drag is null)
            {
                // The first move opens the DragSession; null means another gesture holds history.
                _drag = DragSession.Begin(vm.Timeline.EditorContext, border, CommandNames.MoveLayer, ConfirmDrag, CancelDrag);
                if (_drag is null)
                {
                    CancelDrag();
                    return;
                }

                Scene scene = vm.Timeline.Scene;
                _elements = scene.Children
                    .Where(i => i.ZIndex.Value == vm.Number && !scene.IsElementLocked(i))
                    .ToArray();
            }

            Point position = e.GetPosition(timeline.TimelinePanel);
            var newMargin = new Thickness(0, Math.Max(position.Y - _startRel.Y, 0), 0, 0);

            _newLayer = ViewModel.Timeline.ToLayerNumber(newMargin);

            if (position.Y >= 0)
            {
                vm.PosY.Value = position.Y - _start.Y;
            }
            // Only the clips on screen follow the drag. Scrolling can bring more of them in along the way.
            timeline.RealizeClipsInView(_elements);
            foreach (Element item in _elements)
            {
                if (vm.Timeline.FindViewModelFor(item) is { } realized)
                    realized.Margin.Value = newMargin;
            }

            e.Handled = true;
        }
    }

    private void Border_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // _pressed is only set on a left-button drag start; guard against right-click /
        // single-click reaching here and triggering an accidental MoveLayer to ZIndex 0.
        if (!_pressed) return;

        if (_drag is { } drag)
            drag.Confirm();
        else
            ConfirmDrag();
    }

    // A drag in progress confirms through its DragSession; a press that never moved has nothing to keep.
    private void Border_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_pressed && _drag is null)
            CancelDrag();
    }

    // Escape and Undo put the header and the clips on its row back.
    private void CancelDrag()
    {
        _drag = null;
        _pressed = false;
        Element[] directElements = _elements;
        _elements = [];
        if (DataContext is LayerHeaderViewModel vm)
            RestoreDraggedRows(vm, directElements);
    }

    // Release, capture loss, playback start and detach move the layer to the row the drag reached.
    private void ConfirmDrag()
    {
        _drag = null;
        _pressed = false;
        int newLayerNum = _newLayer;
        Element[] directElements = _elements;
        _elements = [];
        if (DataContext is not LayerHeaderViewModel vm)
            return;

        if (newLayerNum == vm.Number)
        {
            vm.PosY.Value = 0;
            return;
        }

        LayerMovePlan plan = vm.MoveTo(newLayerNum, directElements);

        // A lock-blocked move returns IsNoop without writing the model; snap the
        // dragged header back rather than syncing the UI to an unmoved drop row.
        // PointerMoved already parked the clip views at the drop row, so restore
        // their margins to the (unchanged) model rows too.
        if (plan.IsNoop)
        {
            RestoreDraggedRows(vm, directElements);
            return;
        }

        // The headers stay on their rows and follow the TimelineLayer models the service moved, as they do on undo
        // and redo; only the clip views animate to their new rows.
        foreach (Element item in directElements)
        {
            vm.Timeline.FindViewModelFor(item)?.AnimationRequest(newLayerNum);
        }

        foreach (Element shifted in plan.ShiftedElements)
        {
            ElementViewModel? shiftedVm = vm.Timeline.FindViewModelFor(shifted);
            shiftedVm?.AnimationRequest(shifted.ZIndex.Value);
        }
    }

    private static void RestoreDraggedRows(LayerHeaderViewModel vm, Element[] directElements)
    {
        vm.PosY.Value = 0;
        foreach (Element item in directElements)
        {
            if (vm.Timeline.FindViewModelFor(item) is { } realized)
                realized.Margin.Value = new Thickness(0, vm.Timeline.CalculateLayerTop(item.ZIndex.Value), 0, 0);
        }
    }

    private void Border_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(border);
        if (point.Properties.IsLeftButtonPressed
            && !ViewModel.IsLocked.Value
            && GetOrFindTimeline() is { } timeline)
        {
            // The press need not take the focus from a name box, and a drag changes the layer each row holds, so a
            // name still being typed is saved now, on the layer it was typed for, not when the box loses the focus.
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox focused
                && focused.FindLogicalAncestorOfType<LayerHeader>() is { } editing
                && editing.NameTextBox == focused)
            {
                editing.CommitName();
            }

            _pressed = true;
            _newLayer = ViewModel.Number;
            _startRel = point.Position;
            _start = e.GetCurrentPoint(timeline.TimelinePanel).Position;
        }
    }

    private void OnPositionYChanged()
    {
        if (RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform();
            RenderTransform = translate;
        }

        translate.Y = PositionY;
    }

    private void NameTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            CommitName();
    }

    private void NameTextBox_LostFocus(object? sender, RoutedEventArgs e) => CommitName();

    // Saves the name box's text as the name of the layer on this header's row. The box then shows the name the
    // layer holds, which differs from the text when the text was blank or only the row's number.
    private void CommitName()
    {
        if (DataContext is not LayerHeaderViewModel viewModel) return;

        viewModel.Rename(NameTextBox.Text);
        NameTextBox.SetCurrentValue(TextBox.TextProperty, viewModel.Name.Value);
    }

    private void OnColorConfirmed(SimpleColorPickerButton sender, (Color OldValue, Color NewValue) args)
    {
        if (DataContext is LayerHeaderViewModel viewModel)
        {
            viewModel.SetColor(args.NewValue);
        }
    }
}
