using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;

using BeUtl.ViewModels;

using static BeUtl.Views.Timeline;

using TLVM = BeUtl.ViewModels.TimelineLayerViewModel;

namespace BeUtl.Views;

public sealed partial class LayerHeader : UserControl
{
    private MouseFlags _mouseFlag = MouseFlags.MouseUp;
    private Timeline? _timeline;
    private Point _startRel;
    private Point _start;
    private TLVM[] _layers = Array.Empty<TLVM>();
    private int _newLayer;

    public LayerHeader()
    {
        InitializeComponent();
    }

    private LayerHeaderViewModel ViewModel => (LayerHeaderViewModel)DataContext!;

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        _timeline = this.FindLogicalAncestorOfType<Timeline>();
    }

    private void Border_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_timeline == null || _mouseFlag == MouseFlags.MouseUp)
            return;

        Point position = e.GetPosition(_timeline.TimelinePanel);
        LayerHeaderViewModel vm = ViewModel;
        var newMargin = new Thickness(0, Math.Max(position.Y - _startRel.Y, 0), 0, 0);

        _newLayer = newMargin.ToLayerNumber();

        if (position.Y >= 0)
        {
            vm.PosY.Value = position.Y - _start.Y;
        }
        foreach (TLVM item in _layers)
        {
            item.Margin.Value = newMargin;
        }

        e.Handled = true;
    }

    private void Border_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _mouseFlag = MouseFlags.MouseUp;

        int newLayerNum = _newLayer;
        int oldLayerNum = ViewModel.Number.Value;
        new MoveLayerCommand(ViewModel, newLayerNum, oldLayerNum, _layers).DoAndRecord(CommandRecorder.Default);
        _layers = Array.Empty<TLVM>();
    }

    private void Border_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(border);
        if (point.Properties.IsLeftButtonPressed && _timeline != null)
        {
            _mouseFlag = MouseFlags.MouseDown;
            _startRel = point.Position;
            _start = e.GetCurrentPoint(_timeline.TimelinePanel).Position;
            _layers = ViewModel.Timeline.Layers
                .Where(i => i.Model.ZIndex == ViewModel.Number.Value)
                .ToArray();
        }
    }

    private sealed class MoveLayerCommand : IRecordableCommand
    {
        private readonly int _newLayerNum;
        private readonly int _oldLayerNum;
        private readonly TLVM[] _items1;
        private readonly List<TLVM> _items2;
        private readonly LayerHeaderViewModel _viewModel;
        private readonly List<LayerHeaderViewModel> _viewModels;

        public MoveLayerCommand(LayerHeaderViewModel viewModel, int newLayerNum, int oldLayerNum, TLVM[] items)
        {
            _viewModel = viewModel;
            _newLayerNum = newLayerNum;
            _oldLayerNum = oldLayerNum;
            _items1 = items;
            _items2 = new();
            CoreListMarshal<TLVM> span1 = _viewModel.Timeline.Layers.GetMarshal();
            CoreListMarshal<LayerHeaderViewModel> span2 = _viewModel.Timeline.LayerHeaders.GetMarshal();

            foreach (TLVM item in span1.Value)
            {
                if (item.Model.ZIndex != oldLayerNum
                    && ((item.Model.ZIndex > oldLayerNum && item.Model.ZIndex <= newLayerNum)
                    || (item.Model.ZIndex < oldLayerNum && item.Model.ZIndex >= newLayerNum)))
                {
                    _items2.Add(item);
                }
            }

            _viewModels = new List<LayerHeaderViewModel>();
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

            foreach (TLVM item in _items1)
            {
                item.AnimationRequest(_newLayerNum);
            }

            foreach (TLVM item in CollectionsMarshal.AsSpan(_items2))
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

            foreach (TLVM item in _items1)
            {
                item.AnimationRequest(_oldLayerNum);
            }

            foreach (TLVM item in CollectionsMarshal.AsSpan(_items2))
            {
                item.AnimationRequest(item.Model.ZIndex + x);
            }
        }
    }
}
