using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

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
    private ElementViewModel[] _elements = [];
    private int _newLayer;
    private double _positionY;

    public LayerHeader()
    {
        InitializeComponent();
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
            Point position = e.GetPosition(timeline.TimelinePanel);
            LayerHeaderViewModel vm = ViewModel;
            var newMargin = new Thickness(0, Math.Max(position.Y - _startRel.Y, 0), 0, 0);

            _newLayer = ViewModel.Timeline.ToLayerNumber(newMargin);

            if (position.Y >= 0)
            {
                vm.PosY.Value = position.Y - _start.Y;
            }
            foreach (ElementViewModel item in _elements)
            {
                item.Margin.Value = newMargin;
            }

            e.Handled = true;
        }
    }

    private void Border_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // _pressed is only set on a left-button drag start; guard against right-click /
        // single-click reaching here and triggering an accidental MoveLayer to ZIndex 0.
        if (!_pressed) return;

        _pressed = false;

        LayerHeaderViewModel vm = ViewModel;
        int newLayerNum = _newLayer;
        int oldLayerNum = vm.Number.Value;
        ElementViewModel[] directElements = _elements;
        _elements = [];

        if (newLayerNum == oldLayerNum)
        {
            vm.PosY.Value = 0;
            return;
        }

        // Snapshot the headers shifted by the move (against pre-move state) so we can
        // update their Number.Value after the service rewrites Element.ZIndex.
        List<LayerHeaderViewModel> shiftedHeaders = [];
        foreach (LayerHeaderViewModel item in vm.Timeline.LayerHeaders.GetMarshal().Value)
        {
            int n = item.Number.Value;
            if (n == oldLayerNum) continue;
            if ((oldLayerNum < newLayerNum && n > oldLayerNum && n <= newLayerNum)
                || (oldLayerNum > newLayerNum && n < oldLayerNum && n >= newLayerNum))
            {
                shiftedHeaders.Add(item);
            }
        }

        ILayerMoveService service = vm.Timeline.EditorContext.GetRequiredService<ILayerMoveService>();
        LayerMovePlan plan = service.ApplyMove(
            vm.Timeline.Scene,
            oldLayerNum,
            newLayerNum,
            directElements.Select(x => x.Model).ToArray());

        // A lock-blocked move returns IsNoop without writing the model; snap the
        // dragged header back rather than syncing the UI to an unmoved drop row.
        // PointerMoved already parked the clip views at the drop row, so restore
        // their margins to the (unchanged) model rows too.
        if (plan.IsNoop)
        {
            vm.PosY.Value = 0;
            foreach (ElementViewModel item in directElements)
            {
                item.Margin.Value = new Thickness(0, vm.Timeline.CalculateLayerTop(item.Model.ZIndex), 0, 0);
            }

            return;
        }

        // Service already wrote Element.ZIndex and committed history; now sync VM-side
        // state (Number.Value, header order) and animate the affected element views.
        int headerShift = oldLayerNum < newLayerNum ? -1 : 1;
        vm.UpdateZIndex(newLayerNum);
        foreach (LayerHeaderViewModel item in CollectionsMarshal.AsSpan(shiftedHeaders))
        {
            item.UpdateZIndex(item.Number.Value + headerShift);
        }

        vm.Timeline.LayerHeaders.Move(oldLayerNum, newLayerNum);

        // affectModel: false — ZIndex already written by the service; just drive the visual.
        foreach (ElementViewModel item in directElements)
        {
            item.AnimationRequest(newLayerNum, affectModel: false);
        }

        foreach (Element shifted in plan.ShiftedElements)
        {
            ElementViewModel? shiftedVm = vm.Timeline.Elements.FirstOrDefault(v => v.Model == shifted);
            shiftedVm?.AnimationRequest(shifted.ZIndex, affectModel: false);
        }
    }

    private void Border_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(border);
        if (point.Properties.IsLeftButtonPressed
            && !ViewModel.IsLocked.Value
            && GetOrFindTimeline() is { } timeline)
        {
            _pressed = true;
            _newLayer = ViewModel.Number.Value;
            _startRel = point.Position;
            _start = e.GetCurrentPoint(timeline.TimelinePanel).Position;
            _elements = ViewModel.Timeline.Elements
                .Where(i => i.Model.ZIndex == ViewModel.Number.Value && i.IsEditable.Value)
                .ToArray();
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

    private void OnColorChanged(ColorPickerButton sender, ColorButtonColorChangedEventArgs args)
    {
        if (DataContext is LayerHeaderViewModel viewModel && args.NewColor.HasValue)
        {
            viewModel.SetColor(args.NewColor.Value);
        }
    }
}
