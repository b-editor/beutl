using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.NodeTreeTab.ViewModels;
using Beutl.NodeTree;
using FluentIcons.Common;
using FluentIcons.FluentAvalonia;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.NodeTreeTab.Views;

public partial class SocketView : UserControl
{
    private readonly CompositeDisposable _disposables = [];
    private SocketPoint? _socketPt;
    private StackPanel? _listSocketPanel;
    private NodeView? _nodeView;
    private Canvas? _canvas;
    private Control? _editor;
    private TextBlock? _label;
    private CancellationTokenSource? _updateSocketCts;

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
        _socketPt = null;
        _listSocketPanel = null;
    }

    internal void UpdateSocketPosition()
    {
        if (_nodeView is { DataContext: NodeViewModel nodeViewModel }
            && DataContext is SocketViewModel viewModel
            && nodeViewModel.IsExpanded.Value)
        {
            if (_listSocketPanel != null)
            {
                // ListSocket: update each connected SocketPoint's position individually
                for (int i = 0; i < _listSocketPanel.Children.Count - 1; i++) // skip placeholder
                {
                    if (_listSocketPanel.Children[i] is SocketPoint sp
                        && sp.Tag is ConnectionViewModel connVM)
                    {
                        Point? pos = sp.TranslatePoint(new(5, 5), _nodeView);
                        if (pos.HasValue)
                        {
                            Point canvasPos = pos.Value + nodeViewModel.Position.Value;
                            if (viewModel is InputSocketViewModel)
                                connVM.InputSocketPosition.Value = canvasPos;
                            else
                                connVM.OutputSocketPosition.Value = canvasPos;
                        }
                    }
                }
            }
            else if (_socketPt != null)
            {
                // Single socket
                Point? pos = _socketPt.TranslatePoint(new(5, 5), _nodeView);
                if (pos.HasValue)
                {
                    Point canvasPos = pos.Value + nodeViewModel.Position.Value;
                    if (viewModel is InputSocketViewModel)
                    {
                        foreach (ConnectionViewModel connVM in viewModel.Connections)
                        {
                            connVM.InputSocketPosition.Value = canvasPos;
                        }
                    }
                    else
                    {
                        foreach (ConnectionViewModel connVM in viewModel.Connections)
                        {
                            connVM.OutputSocketPosition.Value = canvasPos;
                        }
                    }
                }
            }
        }
    }

    private void InitSocketPoint(SocketViewModel obj)
    {
        if (obj.Model is IListSocket)
        {
            InitListSocketPoints(obj);
        }
        else
        {
            InitSingleSocketPoint(obj);
        }
    }

    private void InitSingleSocketPoint(SocketViewModel obj)
    {
        _socketPt = new SocketPoint()
        {
            Brush = obj.Color,
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

        AddContextMenu(obj, _socketPt);
        grid.Children.Add(_socketPt);
    }

    private void InitListSocketPoints(SocketViewModel obj)
    {
        _listSocketPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2
        };

        if (obj is InputSocketViewModel)
        {
            Grid.SetColumn(_listSocketPanel, 0);
            _listSocketPanel.Margin = new Thickness(-6, 4, 0, 0);
        }
        else
        {
            Grid.SetColumn(_listSocketPanel, 2);
            _listSocketPanel.Margin = new Thickness(6, 4, 0, 0);
        }

        // Placeholder SocketPoint for new connections
        var placeholder = new SocketPoint
        {
            Brush = obj.Color,
            IsConnected = false,
        };
        placeholder.ConnectRequested += OnSocketPointConnectRequested;
        placeholder.DisconnectRequested += OnSocketPointDisconnectRequested;
        AddContextMenu(obj, placeholder);
        _listSocketPanel.Children.Add(placeholder);

        // Subscribe to Connections changes (handles Add, Remove, Move)
        obj.Connections.ForEachItem(
            (index, connVM) =>
            {
                var socketPt = new SocketPoint
                {
                    Brush = obj.Color,
                    IsConnected = true,
                    Tag = connVM,
                };
                Interaction.GetBehaviors(socketPt).Add(new ListSocketDragBehavior());
                _listSocketPanel.Children.Insert(index, socketPt);
                Dispatcher.UIThread.Post(UpdateSocketPosition, DispatcherPriority.Background);
            },
            (index, connVM) =>
            {
                if (index < _listSocketPanel.Children.Count - 1)
                {
                    _listSocketPanel.Children.RemoveAt(index);
                }
                Dispatcher.UIThread.Post(UpdateSocketPosition, DispatcherPriority.Background);
            },
            () =>
            {
                while (_listSocketPanel.Children.Count > 1)
                {
                    _listSocketPanel.Children.RemoveAt(0);
                }
            })
        .DisposeWith(_disposables);

        grid.Children.Add(_listSocketPanel);
    }

    private void AddContextMenu(SocketViewModel obj, SocketPoint socketPt)
    {
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

        socketPt.ContextMenu = new ContextMenu
        {
            ItemsSource = list
        };
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

    private void OnDataContextAttached(NodeItemViewModel obj)
    {
        if (obj is NodeMonitorViewModel monitorObj)
        {
            InitMonitorContent(monitorObj);
            return;
        }

        InitEditor(obj);

        if (obj is SocketViewModel socketObj)
        {
            InitSocketPoint(socketObj);

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

    private void InitMonitorContent(NodeMonitorViewModel monitorObj)
    {
        switch (monitorObj.Model?.ContentKind)
        {
            case NodeMonitorContentKind.Text:
                {
                    var textBlock = new SelectableTextBlock
                    {
                        FontFamily = new Avalonia.Media.FontFamily("Cascadia Mono, Consolas, monospace"),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    };
                    textBlock.Bind(TextBlock.TextProperty, monitorObj.DisplayText.ToBinding())
                        .DisposeWith(_disposables);
                    Grid.SetColumn(textBlock, 1);
                    grid.Children.Add(textBlock);
                    break;
                }
            case NodeMonitorContentKind.Image:
                {
                    var image = new Image
                    {
                        MaxHeight = 200,
                        MaxWidth = 200,
                        Stretch = Avalonia.Media.Stretch.Uniform,
                        Source = monitorObj.DisplayBitmap
                    };

                    void OnImageInvalidated(object? sender, EventArgs e)
                    {
                        image.Source = monitorObj.DisplayBitmap;
                        image.InvalidateVisual();
                    }

                    monitorObj.ImageInvalidated += OnImageInvalidated;
                    Disposable.Create(() => monitorObj.ImageInvalidated -= OnImageInvalidated)
                        .DisposeWith(_disposables);

                    Grid.SetColumn(image, 1);
                    grid.Children.Add(image);
                    break;
                }
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
            e.IsConnected = viewModel.TryDisconnect(e.Target, e.Connection);
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
