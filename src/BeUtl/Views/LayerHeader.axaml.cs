using System.Reactive.Linq;
using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;

using BeUtl.ViewModels;

using static BeUtl.Views.Timeline;

using TLVM = BeUtl.ViewModels.TimelineLayerViewModel;

namespace BeUtl.Views;

public sealed partial class LayerHeader : UserControl
{
    private MouseFlags _mouseFlag = MouseFlags.MouseUp;
    private Timeline? _timeline;
    private Point _startRel;
    private TLVM[] _layers = Array.Empty<TLVM>();

    public LayerHeader()
    {
        InitializeComponent();
    }

    private LayerHeaderViewModel ViewModel => (LayerHeaderViewModel)DataContext!;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is LayerHeaderViewModel viewModel)
        {
            viewModel.AnimationRequested = async (margin, token) =>
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
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        _timeline = this.FindLogicalAncestorOfType<Timeline>();
    }

    private void Border_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_timeline == null || _mouseFlag == MouseFlags.MouseUp)
            return;

        LayerHeaderViewModel vm = ViewModel;
        var newMargin = new Thickness(
            0,
            Math.Max(e.GetPosition(_timeline.TimelinePanel).Y - _startRel.Y, 0),
            0,
            0);
        vm.Margin.Value = newMargin;
        foreach (TLVM item in _layers)
        {
            item.Margin.Value = newMargin;
        }

        e.Handled = true;
    }

    private void Border_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _mouseFlag = MouseFlags.MouseUp;

        int newLayerNum = ViewModel.Margin.Value.ToLayerNumber();
        int oldLayerNum = ViewModel.Number.Value;
        new MoveLayerCommand(ViewModel, newLayerNum, oldLayerNum, _layers).DoAndRecord(CommandRecorder.Default);
        _layers = Array.Empty<TLVM>();
    }

    private void Border_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(border);
        if (point.Properties.IsLeftButtonPressed)
        {
            _mouseFlag = MouseFlags.MouseDown;
            _startRel = point.Position;
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
            Span<TLVM> span1 = _viewModel.Timeline.Layers.AsSpan();
            Span<LayerHeaderViewModel> span2 = _viewModel.Timeline.LayerHeaders.AsSpan();

            foreach (TLVM item in span1)
            {
                if (item.Model.ZIndex != oldLayerNum
                    && ((item.Model.ZIndex > oldLayerNum && item.Model.ZIndex <= newLayerNum)
                    || (item.Model.ZIndex < oldLayerNum && item.Model.ZIndex >= newLayerNum)))
                {
                    _items2.Add(item);
                }
            }

            _viewModels = new List<LayerHeaderViewModel>();
            foreach (LayerHeaderViewModel item in span2)
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
            foreach (TLVM item in _items1)
            {
                item.AnimationRequest(_oldLayerNum);
            }

            _viewModel.AnimationRequest(_oldLayerNum);

            int x = _oldLayerNum > _newLayerNum ? -1 : 1;
            foreach (TLVM item in CollectionsMarshal.AsSpan(_items2))
            {
                item.AnimationRequest(item.Model.ZIndex + x);
            }

            foreach (LayerHeaderViewModel item in CollectionsMarshal.AsSpan(_viewModels))
            {
                item.AnimationRequest(item.Number.Value + x);
            }
        }
    }
}
