using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.NodeGraph;
using FluentIcons.Common;
using FluentIcons.FluentAvalonia;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.NodeGraphTab.Views;

public partial class NodePortView : UserControl
{
    private readonly CompositeDisposable _disposables = [];
    private NodePortPoint? portPt;
    private StackPanel? _listNodePortPanel;
    private GraphNodeView? _nodeView;
    private Control? _editor;
    private TextBlock? _label;

    public NodePortView()
    {
        InitializeComponent();
        this.SubscribeDataContextChange<NodeMemberViewModel>(OnDataContextAttached, OnDataContextDetached);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _nodeView = this.FindAncestorOfType<GraphNodeView>();
    }

    private void OnDataContextDetached(NodeMemberViewModel obj)
    {
        _disposables.Clear();
        grid.Children.Clear();

        _editor = null;
        _label = null;
        portPt = null;
        _listNodePortPanel = null;
    }

    internal void UpdateNodePortPosition()
    {
        if (_nodeView is { DataContext: GraphNodeViewModel nodeViewModel }
            && DataContext is NodePortViewModel viewModel
            && nodeViewModel.IsExpanded.Value)
        {
            if (_listNodePortPanel != null)
            {
                // ListNodePort: update each connected NodePortPoint's position individually
                for (int i = 0; i < _listNodePortPanel.Children.Count - 1; i++) // skip placeholder
                {
                    if (_listNodePortPanel.Children[i] is NodePortPoint sp
                        && sp.Tag is ConnectionViewModel connVM)
                    {
                        Point? pos = sp.TranslatePoint(new(5, 5), _nodeView);
                        if (pos.HasValue)
                        {
                            Point canvasPos = pos.Value + nodeViewModel.Position.Value;
                            if (viewModel is InputPortViewModel)
                                connVM.InputPortPosition.Value = canvasPos;
                            else
                                connVM.OutputPortPosition.Value = canvasPos;
                        }
                    }
                }
            }
            else if (portPt != null)
            {
                // Single port
                Point? pos = portPt.TranslatePoint(new(5, 5), _nodeView);
                if (pos.HasValue)
                {
                    Point canvasPos = pos.Value + nodeViewModel.Position.Value;
                    if (viewModel is InputPortViewModel)
                    {
                        foreach (ConnectionViewModel connVM in viewModel.Connections)
                        {
                            connVM.InputPortPosition.Value = canvasPos;
                        }
                    }
                    else
                    {
                        foreach (ConnectionViewModel connVM in viewModel.Connections)
                        {
                            connVM.OutputPortPosition.Value = canvasPos;
                        }
                    }
                }
            }
        }
    }

    private void InitNodePortPoint(NodePortViewModel obj)
    {
        if (obj.Model is IListPort)
        {
            InitListNodePortPoints(obj);
        }
        else
        {
            InitSingleNodePortPoint(obj);
        }
    }

    private void InitSingleNodePortPoint(NodePortViewModel obj)
    {
        portPt = new NodePortPoint()
        {
            Brush = obj.Color,
            [!NodePortPoint.IsConnectedProperty] = obj.IsConnected.ToBinding(),
            VerticalAlignment = VerticalAlignment.Top
        };
        portPt.ConnectRequested += OnNodePortPointConnectRequested;
        portPt.DisconnectRequested += OnNodePortPointDisconnectRequested;

        if (obj is InputPortViewModel)
        {
            Grid.SetColumn(portPt, 0);
            portPt.Margin = new Thickness(-6, 4, 0, 0);
        }
        else
        {
            Grid.SetColumn(portPt, 2);
            portPt.Margin = new Thickness(6, 4, 0, 0);
        }

        AddContextMenu(obj, portPt);
        grid.Children.Add(portPt);
    }

    private void InitListNodePortPoints(NodePortViewModel obj)
    {
        _listNodePortPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2
        };

        if (obj is InputPortViewModel)
        {
            Grid.SetColumn(_listNodePortPanel, 0);
            _listNodePortPanel.Margin = new Thickness(-6, 4, 0, 0);
        }
        else
        {
            Grid.SetColumn(_listNodePortPanel, 2);
            _listNodePortPanel.Margin = new Thickness(6, 4, 0, 0);
        }

        // Placeholder NodePortPoint for new connections
        var placeholder = new NodePortPoint
        {
            Brush = obj.Color,
            IsConnected = false,
        };
        placeholder.ConnectRequested += OnNodePortPointConnectRequested;
        placeholder.DisconnectRequested += OnNodePortPointDisconnectRequested;
        AddContextMenu(obj, placeholder);
        _listNodePortPanel.Children.Add(placeholder);

        // Subscribe to Connections changes (handles Add, Remove, Move)
        obj.Connections.ForEachItem(
            (index, connVM) =>
            {
                var portPt = new NodePortPoint
                {
                    Brush = obj.Color,
                    IsConnected = true,
                    Tag = connVM,
                };
                Interaction.GetBehaviors(portPt).Add(new ListPortDragBehavior());
                _listNodePortPanel.Children.Insert(index, portPt);
                Dispatcher.UIThread.Post(UpdateNodePortPosition, DispatcherPriority.Background);
            },
            (index, connVM) =>
            {
                if (index < _listNodePortPanel.Children.Count - 1)
                {
                    _listNodePortPanel.Children.RemoveAt(index);
                }
                Dispatcher.UIThread.Post(UpdateNodePortPosition, DispatcherPriority.Background);
            },
            () =>
            {
                while (_listNodePortPanel.Children.Count > 1)
                {
                    _listNodePortPanel.Children.RemoveAt(0);
                }
            })
        .DisposeWith(_disposables);

        grid.Children.Add(_listNodePortPanel);
    }

    private void AddContextMenu(NodePortViewModel obj, NodePortPoint portPt)
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

        if (obj.Model is IDynamicPort)
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

        portPt.ContextMenu = new ContextMenu
        {
            ItemsSource = list
        };
    }

    private void RenameClick()
    {
        if (DataContext is NodePortViewModel viewModel)
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
            && DataContext is NodePortViewModel viewModel)
        {
            flyout.Confirmed -= OnNameConfirmed;
            viewModel.UpdateName(e);
        }
    }

    private void InitEditor(NodeMemberViewModel obj)
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
                HorizontalAlignment = obj is OutputPortViewModel
                    ? HorizontalAlignment.Right
                    : HorizontalAlignment.Left
            };
            _label.Bind(TextBlock.TextProperty, obj.Name.ToBinding()).DisposeWith(_disposables);

            Grid.SetColumn(_label, 1);
        }
    }

    private void OnDataContextAttached(NodeMemberViewModel obj)
    {
        if (obj is NodeMonitorViewModel monitorObj)
        {
            InitMonitorContent(monitorObj);
            return;
        }

        InitEditor(obj);

        if (obj is NodePortViewModel portObj)
        {
            InitNodePortPoint(portObj);

            portObj.IsConnected
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

            if (obj && DataContext is InputPortViewModel)
            {
                grid.Children.Add(_label);
            }
            else
            {
                grid.Children.Add(_editor ?? _label);
            }
        }
    }

    private void OnNodePortPointDisconnectRequested(object? sender, NodePortConnectRequestedEventArgs e)
    {
        if (DataContext is NodePortViewModel viewModel)
        {
            e.IsConnected = viewModel.TryDisconnect(e.Target, e.Connection);
        }
    }

    private void OnNodePortPointConnectRequested(object? sender, NodePortConnectRequestedEventArgs e)
    {
        if (DataContext is NodePortViewModel viewModel)
        {
            e.IsConnected = viewModel.TryConnect(e.Target);
        }
    }
}
