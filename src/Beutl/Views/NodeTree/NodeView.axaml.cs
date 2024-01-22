using System.ComponentModel;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Beutl.NodeTree;
using Beutl.NodeTree.Nodes.Group;
using Beutl.ViewModels.NodeTree;

using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Views.NodeTree;

public partial class NodeView : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));

    private CancellationTokenSource? _lastTransitionCts;

    private Point _start;
    private bool _captured;
    private Point _snapshot;
    private IDisposable? _positionDisposable;
    private SocketView? _undecidedLeftSocket;
    private SocketView? _undecidedRightSocket;
    private SocketViewModel? _undecidedLeftSocketContext;
    private SocketViewModel? _undecidedRightSocketContext;

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
        void DetachUndecidedSocket(ref SocketView? socket, ref SocketViewModel? socketViewModel)
        {
            if (socket != null)
            {
                stackPanel.Children.Remove(socket);
            }

            socket = null;
            socketViewModel?.Dispose();
            socketViewModel = null;
        }

        _positionDisposable?.Dispose();

        DetachUndecidedSocket(ref _undecidedLeftSocket, ref _undecidedLeftSocketContext);
        DetachUndecidedSocket(ref _undecidedRightSocket, ref _undecidedRightSocketContext);
    }

    private void OnDataContextAttached(NodeViewModel obj)
    {
        _positionDisposable = obj.Position.Subscribe(_ => UpdateSocketPosition());
        if (obj.Node is ISocketsCanBeAdded canBeAdded)
        {
            if (canBeAdded.PossibleLocation.HasFlag(SocketLocation.Left))
            {
                _undecidedLeftSocketContext = new InputSocketViewModel(null, null, obj.Node, obj.EditorContext);
                _undecidedLeftSocket = new SocketView
                {
                    DataContext = _undecidedLeftSocketContext
                };
                stackPanel.Children.Add(_undecidedLeftSocket);
            }

            if (canBeAdded.PossibleLocation.HasFlag(SocketLocation.Right))
            {
                _undecidedRightSocketContext = new OutputSocketViewModel(null, null, obj.Node, obj.EditorContext);
                _undecidedRightSocket = new SocketView
                {
                    DataContext = _undecidedRightSocketContext
                };
                stackPanel.Children.Add(_undecidedRightSocket);
            }
        }
    }

    internal void UpdateSocketPosition()
    {
        if (DataContext is NodeViewModel viewModel)
        {
            if (viewModel.IsExpanded.Value)
            {
                foreach (Control item in itemsControl.GetRealizedContainers())
                {
                    if (item is ContentPresenter { Child: SocketView socketView })
                    {
                        socketView.UpdateSocketPosition();
                    }
                }

                _undecidedLeftSocket?.UpdateSocketPosition();
                _undecidedRightSocket?.UpdateSocketPosition();
            }
            else
            {
                Point vcenter = viewModel.Position.Value + default(Point).WithY(handle.Bounds.Height / 2);
                void UpdatePosition(NodeItemViewModel? viewModel)
                {
                    switch (viewModel)
                    {
                        case InputSocketViewModel input:
                            input.SocketPosition.Value = vcenter;
                            break;
                        case OutputSocketViewModel output:
                            output.SocketPosition.Value = vcenter + default(Point).WithX(Bounds.Width);
                            break;
                    }
                }

                foreach (NodeItemViewModel item in viewModel.Items)
                {
                    UpdatePosition(item);
                }

                UpdatePosition(_undecidedLeftSocketContext);
                UpdatePosition(_undecidedRightSocketContext);
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
            foreach (Control? item in canvas.Children.Where(x => x.DataContext is NodeViewModel))
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
                    IEnumerable<NodeViewModel> selection = GetSelection()
                        .Where(x => x != this && x.DataContext is NodeViewModel)
                        .Select(x => (NodeViewModel)x.DataContext!);
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
            int minZindex = canvas.Children.Where(x => x is NodeView).Min(x => x.ZIndex);
            for (int i = 0; i < canvas.Children.Count; i++)
            {
                Control? item = canvas.Children[i];
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

    private void RenameClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NodeViewModel viewModel)
        {
            var flyout = new RenameFlyout()
            {
                Text = viewModel.Node.Name
            };

            flyout.Confirmed += OnNameConfirmed;

            flyout.ShowAt(handle);
        }
    }

    private void OnNameConfirmed(object? sender, string? e)
    {
        if (sender is RenameFlyout flyout
            && DataContext is NodeViewModel viewModel)
        {
            flyout.Confirmed -= OnNameConfirmed;
            viewModel.UpdateName(e);
        }
    }
}

public sealed class RenameFlyout : PickerFlyoutBase
{
    public static readonly StyledProperty<string?> TextProperty
        = TextBox.TextProperty.AddOwner<RenameFlyout>();

    private TextBox? _textBox;

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public event EventHandler<string?>? Confirmed;

    protected override Control CreatePresenter()
    {
        _textBox ??= new TextBox();
        var pfp = new PickerFlyoutPresenter()
        {
            Width = 240,
            Padding = new(8, 4),
            Content = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = Strings.Rename
                    },
                    _textBox
                }
            }
        };
        pfp.Confirmed += OnFlyoutConfirmed;
        pfp.Dismissed += OnFlyoutDismissed;

        return pfp;
    }

    protected override void OnConfirmed()
    {
        Confirmed?.Invoke(this, _textBox?.Text);
        Hide();
    }

    protected override void OnOpening(CancelEventArgs args)
    {
        base.OnOpening(args);
        _textBox ??= new TextBox();
        _textBox.Text = Text;
    }

    private void OnFlyoutDismissed(PickerFlyoutPresenter sender, object args)
    {
        Hide();
    }

    private void OnFlyoutConfirmed(PickerFlyoutPresenter sender, object args)
    {
        OnConfirmed();
    }
}
