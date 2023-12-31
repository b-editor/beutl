using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

using Beutl.Controls.PropertyEditors;
using Beutl.NodeTree;
using Beutl.ViewModels.NodeTree;

using FluentIcons.Common;
using FluentIcons.FluentAvalonia;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Views.NodeTree;

public partial class SocketView : UserControl
{
    private readonly CompositeDisposable _disposables = [];
    private SocketPoint? _socketPt;
    private NodeView? _nodeView;
    private Canvas? _canvas;
    private Control? _editor;
    private TextBlock? _label;

    public SocketView()
    {
        InitializeComponent();
        this.SubscribeDataContextChange<NodeItemViewModel>(OnDataContextAttached, OnDataContextDetached);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _nodeView = this.FindAncestorOfType<NodeView>();
        _canvas = this.FindAncestorOfType<Canvas>();
    }

    private void OnDataContextDetached(NodeItemViewModel obj)
    {
        _disposables.Clear();
        grid.Children.Clear();

        _editor = null;
        _label = null;
    }

    internal void UpdateSocketPosition()
    {
        if (_socketPt != null
            && _nodeView is { DataContext: NodeViewModel nodeViewModel }
            && DataContext is SocketViewModel viewModel
            && nodeViewModel.IsExpanded.Value)
        {
            Point? pos = _socketPt.TranslatePoint(new(5, 5), _nodeView);
            if (pos.HasValue)
            {
                viewModel.SocketPosition.Value = pos.Value + nodeViewModel.Position.Value;
            }
        }
    }

    private void InitSocketPoint(SocketViewModel obj)
    {
        _socketPt = new SocketPoint()
        {
            [!SocketPoint.BrushProperty] = obj.Brush.ToBinding(),
            [!SocketPoint.IsConnectedProperty] = obj.IsConnected.ToBinding(),
            VerticalAlignment = VerticalAlignment.Top
        };
        _socketPt.ConnectRequested += OnSocketPointConnectRequested;
        _socketPt.DisconnectRequested += OnSocketPointDisconnectRequested;

        if (obj is InputSocketViewModel)
        {
            Grid.SetColumn(_socketPt, 0);
            _socketPt.Margin = new Thickness(-6, 4, 0, 0);
        }
        else
        {
            Grid.SetColumn(_socketPt, 2);
            _socketPt.Margin = new Thickness(6, 4, 0, 0);
        }

        var list = new List<MenuItem>()
        {
            new MenuItem()
            {
                Header = Strings.Disconnect,
                Command = new ReactiveCommand()
                    .WithSubscribe(obj.DisconnectAll)
            }
        };

        if (obj.Model is IAutomaticallyGeneratedSocket)
        {
            list.Add(new MenuItem()
            {
                Header = Strings.Remove,
                Command = new ReactiveCommand()
                    .WithSubscribe(obj.Remove)
            });

            list.Add(new MenuItem()
            {
                Header = Strings.Rename,
                Command = new ReactiveCommand().WithSubscribe(RenameClick),
                Icon = new SymbolIcon
                {
                    Symbol = Symbol.Rename
                }
            });
        }

        _socketPt.ContextMenu = new ContextMenu
        {
            ItemsSource = list
        };

        grid.Children.Add(_socketPt);
    }

    private void RenameClick()
    {
        if (DataContext is SocketViewModel viewModel)
        {
            var flyout = new RenameFlyout()
            {
                Text = viewModel.Model?.Name
            };

            flyout.Confirmed += OnNameConfirmed;

            flyout.ShowAt(this);
        }
    }

    private void OnNameConfirmed(object? sender, string? e)
    {
        if (sender is RenameFlyout flyout
            && DataContext is SocketViewModel viewModel)
        {
            flyout.Confirmed -= OnNameConfirmed;
            viewModel.UpdateName(e);
        }
    }

    private void InitEditor(NodeItemViewModel obj)
    {
        if (obj.PropertyEditorContext is { } propContext)
        {
            PropertyEditorExtension extension = obj.PropertyEditorContext.Extension;
            extension.TryCreateControlForNode(obj.PropertyEditorContext, out Control? control1);
            if (control1 is PropertyEditor pe)
            {
                pe.EditorStyle = PropertyEditorStyle.Compact;
                pe.Bind(PropertyEditor.HeaderProperty, obj.Name.ToBinding()).DisposeWith(_disposables);
            }
            if (control1 != null)
            {
                control1.DataContext = obj.PropertyEditorContext;
                // Todo: TryCreateControlForNodeの時
                (control1).Margin = control1.Margin - new Thickness(4, 0);
            }

            _editor = control1;
            Grid.SetColumn(_editor!, 1);
        }

        if (obj.Model != null)
        {
            _label = new TextBlock
            {
                HorizontalAlignment = obj is OutputSocketViewModel
                    ? HorizontalAlignment.Right
                    : HorizontalAlignment.Left
            };
            _label.Bind(TextBlock.TextProperty, obj.Name.ToBinding()).DisposeWith(_disposables);

            Grid.SetColumn(_label, 1);
        }
    }

    private void OnSocketDisconnected(EventPattern<SocketConnectionChangedEventArgs> obj)
    {
        SocketConnectionChangedEventArgs e = obj.EventArgs;
        RemoveConnectionLine(e.Connection);
    }

    private void RemoveConnectionLine(Connection connection)
    {
        if (_canvas is { })
        {
            for (int i = _canvas.Children.Count - 1; i >= 0; i--)
            {
                Control item = _canvas.Children[i];
                if (item is ConnectionLine line
                    && line.Match(connection.Input, connection.Output))
                {
                    _canvas.Children.RemoveAt(i);
                }
            }
        }
    }

    private void AddConnectionLine(Connection connection)
    {
        if (_canvas is { DataContext: NodeTreeViewModel treeViewModel }
            && treeViewModel.FindSocketViewModel(connection.Input) is InputSocketViewModel inputViewModel
            && treeViewModel.FindSocketViewModel(connection.Output) is OutputSocketViewModel outputViewModel
            && !_canvas.Children.OfType<ConnectionLine>().Any(x => x.Match(inputViewModel, outputViewModel)))
        {
            _canvas.Children.Insert(0, NodeTreeView.CreateLine(inputViewModel, outputViewModel));
        }
    }

    private void OnSocketConnected(EventPattern<SocketConnectionChangedEventArgs> obj)
    {
        SocketConnectionChangedEventArgs e = obj.EventArgs;
        AddConnectionLine(e.Connection);
    }

    private void OnDataContextAttached(NodeItemViewModel obj)
    {
        InitEditor(obj);

        if (obj is SocketViewModel socketObj)
        {
            InitSocketPoint(socketObj);
            if (socketObj.Model != null)
            {
                Observable.FromEventPattern<SocketConnectionChangedEventArgs>(x => socketObj.Model.Connected += x, x => socketObj.Model.Connected -= x)
                    .ObserveOnUIDispatcher()
                    .Subscribe(OnSocketConnected)
                    .DisposeWith(_disposables);

                Observable.FromEventPattern<SocketConnectionChangedEventArgs>(x => socketObj.Model.Disconnected += x, x => socketObj.Model.Disconnected -= x)
                    .ObserveOnUIDispatcher()
                    .Subscribe(OnSocketDisconnected)
                    .DisposeWith(_disposables);
            }

            socketObj.IsConnected
                .ObserveOnUIDispatcher()
                .Subscribe(OnIsConnectedChanged)
                .DisposeWith(_disposables);
        }
        else if (_label != null)
        {
            grid.Children.Add(_editor ?? _label);
        }
    }

    private void OnIsConnectedChanged(bool obj)
    {
        if (_label != null)
        {
            grid.Children.Remove(_label);
            if (_editor != null)
                grid.Children.Remove(_editor);

            if (obj && DataContext is InputSocketViewModel)
            {
                grid.Children.Add(_label);
            }
            else
            {
                grid.Children.Add(_editor ?? _label);
            }
        }
    }

    private void OnSocketPointDisconnectRequested(object? sender, SocketConnectRequestedEventArgs e)
    {
        if (DataContext is SocketViewModel viewModel)
        {
            e.IsConnected = viewModel.TryDisconnect(e.Target);
        }
    }

    private void OnSocketPointConnectRequested(object? sender, SocketConnectRequestedEventArgs e)
    {
        if (DataContext is SocketViewModel viewModel)
        {
            e.IsConnected = viewModel.TryConnect(e.Target);
        }
    }
}
