using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Group;

namespace Beutl.Editor.Components.NodeGraphTab.Views;

public partial class GraphNodeView : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));

    private CancellationTokenSource? _lastTransitionCts;

    private Point _start;
    private bool _captured;
    private Point _snapshot;
    private IDisposable? _positionDisposable;
    private NodePortView? _undecidedLeftNodePort;
    private NodePortView? _undecidedRightNodePort;
    private NodePortViewModel? _undecidedLeftNodePortContext;
    private NodePortViewModel? _undecidedRightNodePortContext;

    public GraphNodeView()
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

        this.SubscribeDataContextChange<GraphNodeViewModel>(OnDataContextAttached, OnDataContextDetached);

        SizeChanged += OnSizeChanged;
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateNodePortPosition();
    }

    private void OnDataContextDetached(GraphNodeViewModel obj)
    {
        void DetachUndecidedNodePort(ref NodePortView? port, ref NodePortViewModel? portViewModel)
        {
            if (port != null)
            {
                stackPanel.Children.Remove(port);
            }

            port = null;
            portViewModel?.Dispose();
            portViewModel = null;
        }

        _positionDisposable?.Dispose();

        DetachUndecidedNodePort(ref _undecidedLeftNodePort, ref _undecidedLeftNodePortContext);
        DetachUndecidedNodePort(ref _undecidedRightNodePort, ref _undecidedRightNodePortContext);
    }

    private void OnDataContextAttached(GraphNodeViewModel obj)
    {
        _positionDisposable = obj.Position.Subscribe(_ => UpdateNodePortPosition());
        if (obj.GraphNode is IDynamicPortNode canBeAdded)
        {
            if (canBeAdded.PossibleLocation.HasFlag(NodePortLocation.Left))
            {
                _undecidedLeftNodePortContext = new InputPortViewModel(null, null, obj);
                _undecidedLeftNodePort = new NodePortView
                {
                    DataContext = _undecidedLeftNodePortContext
                };
                stackPanel.Children.Add(_undecidedLeftNodePort);
            }

            if (canBeAdded.PossibleLocation.HasFlag(NodePortLocation.Right))
            {
                _undecidedRightNodePortContext = new OutputPortViewModel(null, null, obj);
                _undecidedRightNodePort = new NodePortView
                {
                    DataContext = _undecidedRightNodePortContext
                };
                stackPanel.Children.Add(_undecidedRightNodePort);
            }
        }
    }

    internal void UpdateNodePortPosition()
    {
        if (DataContext is GraphNodeViewModel viewModel)
        {
            if (viewModel.IsExpanded.Value)
            {
                foreach (Control item in itemsControl.GetRealizedContainers())
                {
                    if (item is ContentPresenter { Child: NodePortView portView })
                    {
                        portView.UpdateNodePortPosition();
                    }
                }

                _undecidedLeftNodePort?.UpdateNodePortPosition();
                _undecidedRightNodePort?.UpdateNodePortPosition();
            }
            else
            {
                Point vcenter = viewModel.Position.Value + default(Point).WithY(handle.Bounds.Height / 2);
                Point vcenterRight = vcenter + default(Point).WithX(Bounds.Width);
                void UpdatePosition(NodeMemberViewModel? viewModel)
                {
                    switch (viewModel)
                    {
                        case InputPortViewModel input:
                            foreach (ConnectionViewModel connVM in input.Connections)
                            {
                                connVM.InputPortPosition.Value = vcenter;
                            }
                            break;
                        case OutputPortViewModel output:
                            foreach (ConnectionViewModel connVM in output.Connections)
                            {
                                connVM.OutputPortPosition.Value = vcenterRight;
                            }
                            break;
                    }
                }

                foreach (NodeMemberViewModel item in viewModel.Items)
                {
                    UpdatePosition(item);
                }

                UpdatePosition(_undecidedLeftNodePortContext);
                UpdatePosition(_undecidedRightNodePortContext);
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

            if (DataContext is GraphNodeViewModel viewModel)
            {
                viewModel.Position.Value = new(left, top);
            }

            foreach (GraphNodeView? item in GetSelection())
            {
                if (item != this && item.DataContext is GraphNodeViewModel itemViewModel)
                {
                    itemViewModel.Position.Value += delta;
                }
            }

            e.Handled = true;
        }
    }

    private IEnumerable<GraphNodeView> GetSelection()
    {
        if (Parent is Canvas canvas)
        {
            return canvas.Children.Where(x => x is GraphNodeView { DataContext: GraphNodeViewModel { IsSelected.Value: true } })
                .OfType<GraphNodeView>();
        }
        else
        {
            return [];
        }
    }

    private void ClearSelection()
    {
        if (Parent is Canvas canvas)
        {
            foreach (Control? item in canvas.Children.Where(x => x.DataContext is GraphNodeViewModel))
            {
                if (item.DataContext is GraphNodeViewModel itemViewModel)
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
            if (DataContext is GraphNodeViewModel viewModel)
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
                    IEnumerable<GraphNodeViewModel> selection = GetSelection()
                        .Where(x => x != this && x.DataContext is GraphNodeViewModel)
                        .Select(x => (GraphNodeViewModel)x.DataContext!);
                    viewModel.UpdatePosition(selection);
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
            _start = e.GetPosition(Parent as Visual);
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
            int minZindex = canvas.Children.Where(x => x is GraphNodeView).Min(x => x.ZIndex);
            for (int i = 0; i < canvas.Children.Count; i++)
            {
                Control? item = canvas.Children[i];
                if (item is GraphNodeView)
                {
                    item.ZIndex -= minZindex;
                }
            }
        }
    }

    private void OpenNodeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GraphNodeViewModel { GraphNode: GroupNode groupNode }
            && this.FindAncestorOfType<NodeGraphTabView>()?.DataContext is NodeGraphTabViewModel tabViewModel)
        {
            tabViewModel.NavigateTo(groupNode.Group);
        }
    }

    private void RenameClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GraphNodeViewModel viewModel)
        {
            var flyout = new RenameFlyout()
            {
                Text = viewModel.GraphNode.Name
            };

            flyout.Confirmed += OnNameConfirmed;

            flyout.ShowAt(handle);
        }
    }

    private void OnNameConfirmed(object? sender, string? e)
    {
        if (sender is RenameFlyout flyout
            && DataContext is GraphNodeViewModel viewModel)
        {
            flyout.Confirmed -= OnNameConfirmed;
            viewModel.UpdateName(e);
        }
    }
}
