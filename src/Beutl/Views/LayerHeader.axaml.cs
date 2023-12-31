using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;

using Beutl.ViewModels;

using FluentAvalonia.UI.Controls;

using ElementViewModel = Beutl.ViewModels.ElementViewModel;

namespace Beutl.Views;

public sealed partial class LayerHeader : UserControl
{
    public static readonly DirectProperty<LayerHeader, double> PositionYProperty
        = AvaloniaProperty.RegisterDirect<LayerHeader, double>(
            nameof(PositionY),
            o => o.PositionY,
            (o, v) => o.PositionY = v);

    private bool _pressed;
    private Timeline? _timeline;
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

    private Timeline? GetOrFindTimeline()
    {
        return _timeline ??= this.FindLogicalAncestorOfType<Timeline>();
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
        _pressed = false;

        int newLayerNum = _newLayer;
        int oldLayerNum = ViewModel.Number.Value;
        new MoveLayerCommand(ViewModel, newLayerNum, oldLayerNum, _elements).DoAndRecord(CommandRecorder.Default);
        _elements = [];
    }

    private void Border_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(border);
        if (point.Properties.IsLeftButtonPressed && GetOrFindTimeline() is { } timeline)
        {
            _pressed = true;
            _startRel = point.Position;
            _start = e.GetCurrentPoint(timeline.TimelinePanel).Position;
            _elements = ViewModel.Timeline.Elements
                .Where(i => i.Model.ZIndex == ViewModel.Number.Value)
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

    private sealed class MoveLayerCommand : IRecordableCommand
    {
        private readonly int _newLayerNum;
        private readonly int _oldLayerNum;
        private readonly ElementViewModel[] _items1;
        private readonly List<ElementViewModel> _items2;
        private readonly LayerHeaderViewModel _viewModel;
        private readonly List<LayerHeaderViewModel> _viewModels;

        public MoveLayerCommand(LayerHeaderViewModel viewModel, int newLayerNum, int oldLayerNum, ElementViewModel[] items)
        {
            _viewModel = viewModel;
            _newLayerNum = newLayerNum;
            _oldLayerNum = oldLayerNum;
            _items1 = items;
            _items2 = [];
            CoreListMarshal<ElementViewModel> span1 = _viewModel.Timeline.Elements.GetMarshal();
            CoreListMarshal<LayerHeaderViewModel> span2 = _viewModel.Timeline.LayerHeaders.GetMarshal();

            foreach (ElementViewModel item in span1.Value)
            {
                if (item.Model.ZIndex != oldLayerNum
                    && ((item.Model.ZIndex > oldLayerNum && item.Model.ZIndex <= newLayerNum)
                    || (item.Model.ZIndex < oldLayerNum && item.Model.ZIndex >= newLayerNum)))
                {
                    _items2.Add(item);
                }
            }

            _viewModels = [];
            foreach (LayerHeaderViewModel item in span2.Value)
            {
                if (item.Number.Value != oldLayerNum
                    && ((item.Number.Value > oldLayerNum && item.Number.Value <= newLayerNum)
                    || (item.Number.Value < oldLayerNum && item.Number.Value >= newLayerNum)))
                {
                    _viewModels.Add(item);
                }
            }
        }

        public void Do()
        {
            int x = _newLayerNum > _oldLayerNum ? -1 : 1;
            _viewModel.AnimationRequest(_newLayerNum);
            foreach (LayerHeaderViewModel item in CollectionsMarshal.AsSpan(_viewModels))
            {
                item.AnimationRequest(item.Number.Value + x);
            }

            _viewModel.Timeline.LayerHeaders.Move(_oldLayerNum, _newLayerNum);

            foreach (ElementViewModel item in _items1)
            {
                item.AnimationRequest(_newLayerNum);
            }

            foreach (ElementViewModel item in CollectionsMarshal.AsSpan(_items2))
            {
                item.AnimationRequest(item.Model.ZIndex + x);
            }
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            int x = _oldLayerNum > _newLayerNum ? -1 : 1;
            _viewModel.AnimationRequest(_oldLayerNum);
            foreach (LayerHeaderViewModel item in CollectionsMarshal.AsSpan(_viewModels))
            {
                item.AnimationRequest(item.Number.Value + x);
            }

            _viewModel.Timeline.LayerHeaders.Move(_newLayerNum, _oldLayerNum);

            foreach (ElementViewModel item in _items1)
            {
                item.AnimationRequest(_oldLayerNum);
            }

            foreach (ElementViewModel item in CollectionsMarshal.AsSpan(_items2))
            {
                item.AnimationRequest(item.Model.ZIndex + x);
            }
        }
    }
}
