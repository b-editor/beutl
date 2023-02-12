using System.Diagnostics.CodeAnalysis;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

using Beutl.Controls.PropertyEditors;
using Beutl.Framework;
using Beutl.NodeTree;
using Beutl.ViewModels.NodeTree;

using Reactive.Bindings.Extensions;

namespace Beutl.Views.NodeTree;

public partial class SocketView : UserControl
{
    private readonly CompositeDisposable _disposables = new();
    private SocketPoint? _socketPt;
    private NodeView? _nodeView;
    private Canvas? _canvas;
    private IControl? _editor;
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

    private static string GetSocketName(INodeItem item)
    {
        string? name = (item as CoreObject)?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = null;
        }

        if (item.Property is { } property)
        {
            CorePropertyMetadata metadata = property.Property.GetMetadata<CorePropertyMetadata>(property.ImplementedType);

            return metadata.DisplayAttribute?.GetName() ?? name ?? property.Property.Name;
        }
        else
        {
            return name ?? "Unknown";
        }
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
        grid.Children.Add(_socketPt);
    }

    private void InitEditor(NodeItemViewModel obj)
    {
        if (obj.PropertyEditorContext is { } propContext)
        {
            PropertyEditorExtension extension = obj.PropertyEditorContext.Extension;
            extension.TryCreateControlForNode(obj.PropertyEditorContext, out IControl? control1);
            if (control1 is PropertyEditor pe)
            {
                pe.UseCompact = true;
            }

            _editor = control1;
            Grid.SetColumn((Control)_editor!, 1);
        }

        _label = new TextBlock
        {
            Text = GetSocketName(obj.Model),
            HorizontalAlignment = obj is OutputSocketViewModel
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left
        };

        Grid.SetColumn(_label, 1);
    }

    private void OnSocketDisconnected(EventPattern<SocketConnectionChangedEventArgs> obj)
    {
        SocketConnectionChangedEventArgs e = obj.EventArgs;
        if (_canvas is { }
            && DataContext is SocketViewModel viewModel)
        {
            ISocket currentSocket = viewModel.Model;
            ISocket anotherSocket = viewModel.Model == e.Connection.Output
                ? e.Connection.Input
                : e.Connection.Output;

            for (int i = _canvas.Children.Count - 1; i >= 0; i--)
            {
                IControl item = _canvas.Children[i];
                if (item is ConnectionLine line
                    && line.Match(currentSocket, anotherSocket))
                {
                    _canvas.Children.RemoveAt(i);
                }
            }
        }
    }

    private void AddConnectionLine(ISocket anotherSocket)
    {
        if (_canvas is { DataContext: NodeTreeTabViewModel tabViewModel }
            && DataContext is SocketViewModel viewModel)
        {
            ISocket currentSocket = viewModel.Model;
            SocketViewModel? anotherViewModel = tabViewModel.FindSocketViewModel(anotherSocket);
            if (anotherViewModel == null)
                return;

            if (!_canvas.Children.OfType<ConnectionLine>().Any(x => x.Match(currentSocket, anotherSocket))
                && SortSocket(
                    viewModel, anotherViewModel,
                    out InputSocketViewModel? inputViewModel, out OutputSocketViewModel? outputViewModel))
            {
                _canvas.Children.Insert(0, NodeTreeTab.CreateLine(inputViewModel, outputViewModel));
            }
        }
    }

    private void OnSocketConnected(EventPattern<SocketConnectionChangedEventArgs> obj)
    {
        SocketConnectionChangedEventArgs e = obj.EventArgs;
        if (DataContext is SocketViewModel viewModel)
        {
            ISocket anotherSocket = viewModel.Model == e.Connection.Output
                ? e.Connection.Input
                : e.Connection.Output;
            AddConnectionLine(anotherSocket);
        }
    }

    private void OnDataContextAttached(NodeItemViewModel obj)
    {
        InitEditor(obj);

        if (obj is SocketViewModel socketObj)
        {
            InitSocketPoint(socketObj);
            Observable.FromEventPattern<SocketConnectionChangedEventArgs>(x => socketObj.Model.Connected += x, x => socketObj.Model.Connected -= x)
                .ObserveOnUIDispatcher()
                .Subscribe(OnSocketConnected)
                .DisposeWith(_disposables);

            Observable.FromEventPattern<SocketConnectionChangedEventArgs>(x => socketObj.Model.Disconnected += x, x => socketObj.Model.Disconnected -= x)
                .ObserveOnUIDispatcher()
                .Subscribe(OnSocketDisconnected)
                .DisposeWith(_disposables);

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

    private static bool SortSocket(
        ISocket first, ISocket second,
        [NotNullWhen(true)] out IInputSocket? inputSocket,
        [NotNullWhen(true)] out IOutputSocket? outputSocket)
    {
        if (first is IInputSocket input)
        {
            inputSocket = input;
            outputSocket = second as IOutputSocket;
        }
        else
        {
            inputSocket = second as IInputSocket;
            outputSocket = first as IOutputSocket;
        }

        return outputSocket != null && inputSocket != null;
    }

    private static bool SortSocket(
        SocketViewModel first, SocketViewModel second,
        [NotNullWhen(true)] out InputSocketViewModel? inputSocket,
        [NotNullWhen(true)] out OutputSocketViewModel? outputSocket)
    {
        if (first is InputSocketViewModel input)
        {
            inputSocket = input;
            outputSocket = second as OutputSocketViewModel;
        }
        else
        {
            inputSocket = second as InputSocketViewModel;
            outputSocket = first as OutputSocketViewModel;
        }

        return outputSocket != null && inputSocket != null;
    }

    private void OnSocketPointDisconnectRequested(object? sender, SocketConnectRequestedEventArgs e)
    {
        if (DataContext is SocketViewModel viewModel
            && SortSocket(
                viewModel.Model, e.Target,
                out IInputSocket? inputSocket, out IOutputSocket? outputSocket))
        {
            var command = new DisconnectCommand(inputSocket, outputSocket);
            command.DoAndRecord(CommandRecorder.Default);
            e.IsConnected = false;
        }
    }

    private void OnSocketPointConnectRequested(object? sender, SocketConnectRequestedEventArgs e)
    {
        if (DataContext is SocketViewModel viewModel
            && SortSocket(
                viewModel.Model, e.Target,
                out IInputSocket? inputSocket, out IOutputSocket? outputSocket))
        {
            var command = new ConnectCommand(inputSocket, outputSocket);
            command.DoAndRecord(CommandRecorder.Default);
            e.IsConnected = command.IsConnected;
        }
    }

    private sealed class DisconnectCommand : IRecordableCommand
    {
        private readonly IInputSocket _inputSocket;
        private readonly IOutputSocket _outputSocket;

        public DisconnectCommand(IInputSocket inputSocket, IOutputSocket outputSocket)
        {
            _inputSocket = inputSocket;
            _outputSocket = outputSocket;
        }

        public void Do()
        {
            _outputSocket.Disconnect(_inputSocket);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _outputSocket.TryConnect(_inputSocket);
        }
    }

    private sealed class ConnectCommand : IRecordableCommand
    {
        private readonly IInputSocket _inputSocket;
        private readonly IOutputSocket _outputSocket;

        public ConnectCommand(IInputSocket inputSocket, IOutputSocket outputSocket)
        {
            _inputSocket = inputSocket;
            _outputSocket = outputSocket;
        }

        public bool IsConnected { get; set; }

        public void Do()
        {
            IsConnected = _outputSocket.TryConnect(_inputSocket);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _outputSocket.Disconnect(_inputSocket);
        }
    }
}
