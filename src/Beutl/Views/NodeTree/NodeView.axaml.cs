using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Generators;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;

using Beutl.NodeTree.Nodes.Group;
using Beutl.ViewModels.NodeTree;

namespace Beutl.Views.NodeTree;

public partial class NodeView : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));

    private CancellationTokenSource? _lastTransitionCts;

    private Point _start;
    private bool _captured;
    private Point _snapshot;
    private IDisposable? _positionDisposable;
    private SocketView? _undecidedSocket;
    private SocketViewModel? _undecidedSocketContext;

    public NodeView()
    {
        InitializeComponent();

        handle.PointerPressed += OnHandlePointerPressed;
        handle.PointerReleased += OnHandlePointerReleased;
        handle.PointerMoved += OnHandlePointerMoved;
        nodeContent.PointerPressed += OnNodeContentPointerPressed;
        nodeContent.PointerReleased += OnNodeContentPointerReleased;
        ContextRequested += OnContextRequested;

        expandToggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(v =>
            {
                _lastTransitionCts?.Cancel();
                _lastTransitionCts = new CancellationTokenSource();
                CancellationToken localToken = _lastTransitionCts.Token;

                nodeContent.IsVisible = v == true;

                _ = s_transition.Start(null, this, localToken);
            });

        this.SubscribeDataContextChange<NodeViewModel>(OnDataContextAttached, OnDataContextDetached);

        SizeChanged += OnSizeChanged;
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateSocketPosition();
    }

    private void OnDataContextDetached(NodeViewModel obj)
    {
        _positionDisposable?.Dispose();

        _undecidedSocket = null;
        _undecidedSocketContext?.Dispose();
        _undecidedSocketContext = null;
        stackPanel.Children.RemoveRange(1, stackPanel.Children.Count - 1);
    }

    private void OnDataContextAttached(NodeViewModel obj)
    {
        _positionDisposable = obj.Position.Subscribe(_ => UpdateSocketPosition());
        if (obj.Node is GroupInput or GroupOutput)
        {
            _undecidedSocketContext = obj.Node is GroupInput
                    ? new OutputSocketViewModel(null, null, obj.Node)
                    : new InputSocketViewModel(null, null, obj.Node);
            _undecidedSocket = new SocketView
            {
                DataContext = _undecidedSocketContext
            };
            stackPanel.Children.Add(_undecidedSocket);
        }
    }

    internal void UpdateSocketPosition()
    {
        if (DataContext is NodeViewModel viewModel)
        {
            if (viewModel.IsExpanded.Value)
            {
                foreach (ItemContainerInfo item in itemsControl.ItemContainerGenerator.Containers)
                {
                    if (item.ContainerControl is ContentPresenter { Child: SocketView socketView })
                    {
                        socketView.UpdateSocketPosition();
                    }
                }

                _undecidedSocket?.UpdateSocketPosition();
            }
            else
            {
                Point vcenter = viewModel.Position.Value + default(Point).WithY(handle.Bounds.Height / 2);
                foreach (NodeItemViewModel item in viewModel.Items)
                {
                    switch (item)
                    {
                        case InputSocketViewModel input:
                            input.SocketPosition.Value = vcenter;
                            break;
                        case OutputSocketViewModel output:
                            output.SocketPosition.Value = vcenter + default(Point).WithX(Bounds.Width);
                            break;
                    }
                }

                switch (_undecidedSocketContext)
                {
                    case InputSocketViewModel input:
                        input.SocketPosition.Value = vcenter;
                        break;
                    case OutputSocketViewModel output:
                        output.SocketPosition.Value = vcenter + default(Point).WithX(Bounds.Width);
                        break;
                }
            }
        }
    }

    private void OnNodeContentPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        OnReleased();
        e.Handled = true;
    }

    private void OnNodeContentPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        OnPressed();
        e.Handled = true;
    }

    private void OnHandlePointerMoved(object? sender, PointerEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(this);
        if (_captured
            && point.Properties.IsLeftButtonPressed
            && Parent is Canvas canvas)
        {
            Point position = e.GetPosition(canvas);
            Point delta = position - _start;
            _start = position;
            double left = Canvas.GetLeft(this) + delta.X;
            double top = Canvas.GetTop(this) + delta.Y;

            if (DataContext is NodeViewModel viewModel)
            {
                viewModel.Position.Value = new(left, top);
            }

            foreach (NodeView? item in GetSelection())
            {
                if (item != this && item.DataContext is NodeViewModel itemViewModel)
                {
                    itemViewModel.Position.Value += delta;
                }
            }

            e.Handled = true;
        }
    }

    private IEnumerable<NodeView> GetSelection()
    {
        if (Parent is Canvas canvas)
        {
            return canvas.Children.Where(x => x is NodeView { DataContext: NodeViewModel { IsSelected.Value: true } })
                .OfType<NodeView>();
        }
        else
        {
            return Enumerable.Empty<NodeView>();
        }
    }

    private void ClearSelection()
    {
        if (Parent is Canvas canvas)
        {
            foreach (IControl? item in canvas.Children.Where(x => x.DataContext is NodeViewModel))
            {
                if (item.DataContext is NodeViewModel itemViewModel)
                {
                    itemViewModel.IsSelected.Value = false;
                }
            }
        }
    }

    private void OnHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_captured)
        {
            _captured = false;
            e.Handled = true;

            OnReleased();
            if (DataContext is NodeViewModel viewModel)
            {
                if (_snapshot.NearlyEquals(GetPoint()))
                {
                    if (e.KeyModifiers == KeyModifiers.Control)
                    {
                        viewModel.IsSelected.Value = !viewModel.IsSelected.Value;
                    }
                    else
                    {
                        ClearSelection();
                    }
                }
                else
                {
                    viewModel.NotifyPositionChange();
                    foreach (NodeView? item in GetSelection())
                    {
                        if (item != this && item.DataContext is NodeViewModel itemViewModel)
                        {
                            itemViewModel.NotifyPositionChange();
                        }
                    }
                }
            }
        }
    }

    public Point GetPoint()
    {
        return new(Canvas.GetLeft(this), Canvas.GetTop(this));
    }

    private void OnHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            _start = e.GetPosition(Parent);
            _captured = true;
            _snapshot = GetPoint();

            e.Handled = true;
            OnPressed();
        }
    }

    private void OnPressed()
    {
        if (Parent is Canvas canvas)
        {
            ZIndex = canvas.Children.Max(x => x.ZIndex) + 1;
        }
    }

    private void OnReleased()
    {
        if (Parent is Canvas canvas)
        {
            int minZindex = canvas.Children.Where(x => x is NodeView).Min(x => x.ZIndex);
            for (int i = 0; i < canvas.Children.Count; i++)
            {
                IControl? item = canvas.Children[i];
                if (item is NodeView)
                {
                    item.ZIndex -= minZindex;
                }
            }
        }
    }

    private void OpenNodeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NodeViewModel { Node: GroupNode groupNode }
            && this.FindAncestorOfType<NodeTreeTab>()?.DataContext is NodeTreeTabViewModel tabViewModel)
        {
            tabViewModel.NavigateTo(groupNode.Group);
        }
    }
}
