using System.Diagnostics.CodeAnalysis;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

using Beutl.Controls.PropertyEditors;
using Beutl.Framework;
using Beutl.NodeTree;
using Beutl.ViewModels.NodeTree;

namespace Beutl.Views.NodeTree;

public partial class SocketView : UserControl
{
    private readonly CompositeDisposable _disposables = new();
    private SocketPoint? _socketPt;
    private NodeView? _nodeView;
    private Canvas? _canvas;
    private IControl? _editor;
    private TextBlock? _label;
    private bool _firstArrange = true;

    public SocketView()
    {
        InitializeComponent();
        this.SubscribeDataContextChange<SocketViewModel>(OnDataContextAttached, OnDataContextDetached);
    }

    private void SocketView_LayoutUpdated(object? sender, EventArgs e)
    {
        LayoutUpdated -= SocketView_LayoutUpdated;
        UpdateSocketPosition();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_firstArrange)
        {
            LayoutUpdated += SocketView_LayoutUpdated;
            _firstArrange = false;
        }

        return base.ArrangeOverride(finalSize);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _firstArrange = true;
        _nodeView = this.FindAncestorOfType<NodeView>();
        _canvas = this.FindAncestorOfType<Canvas>();

        if (_nodeView?.DataContext is NodeViewModel nodeViewModel)
        {
            nodeViewModel.Position.Subscribe(_ => UpdateSocketPosition());
            nodeViewModel.IsExpanded.Subscribe(_ => UpdateSocketPosition());
        }
    }

    private void OnDataContextDetached(SocketViewModel obj)
    {
        obj.Model.Connected -= OnSocketConnected;
        obj.Model.Disconnected -= OnSocketDisconnected;
        _disposables.Clear();
        grid.Children.Clear();

        _editor = null;
        _label = null;
    }

    private static string GetSocketName(ISocket socket)
    {
        string? name = (socket as CoreObject)?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = null;
        }

        if (socket.Property is { } property)
        {
            CorePropertyMetadata metadata = property.Property.GetMetadata<CorePropertyMetadata>(property.ImplementedType);

            return metadata.DisplayAttribute?.GetName() ?? name ?? property.Property.Name;
        }
        else
        {
            return name ?? "Unknown";
        }
    }

    private void UpdateSocketPosition()
    {
        if (_socketPt != null
            && _nodeView is { DataContext: NodeViewModel nodeViewModel }
            && DataContext is SocketViewModel viewModel)
        {
            if (nodeViewModel.IsExpanded.Value)
            {
                Point? pos = _socketPt.TranslatePoint(new(5, 5), _nodeView);
                if (pos.HasValue)
                {
                    viewModel.SocketPosition.Value = pos.Value + nodeViewModel.Position.Value;
                }
            }
            else
            {
                Point vcenter = nodeViewModel.Position.Value + default(Point).WithY(_nodeView.handle.Bounds.Height / 2);
                switch (viewModel)
                {
                    case InputSocketViewModel:
                        viewModel.SocketPosition.Value = vcenter;
                        break;
                    case OutputSocketViewModel:
                        viewModel.SocketPosition.Value = vcenter + default(Point).WithX(_nodeView.Bounds.Width);
                        break;
                }
            }
        }
    }

    private void InitSocketPoint(SocketViewModel obj)
    {
        _socketPt = new SocketPoint()
        {
            [!SocketPoint.StateProperty] = obj.State.ToBinding(),
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

    private void InitEditor(SocketViewModel obj)
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
        }

        _label = new TextBlock
        {
            Text = GetSocketName(obj.Model),
            HorizontalAlignment = obj is OutputSocketViewModel
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left
        };

        var control = (Control)(_editor ?? _label);
        Grid.SetColumn(control, 1);
    }

    private void OnSocketDisconnected(object? sender, SocketConnectionChangedEventArgs e)
    {
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

            if (!_canvas.Children.OfType<ConnectionLine>().Any(x => x.Match(currentSocket, anotherSocket)))
            {
                _canvas.Children.Insert(0, NodeTreeTab.CreateLine(viewModel, anotherViewModel));
            }
        }
    }

    private void OnSocketConnected(object? sender, SocketConnectionChangedEventArgs e)
    {
        if (DataContext is SocketViewModel viewModel)
        {
            ISocket anotherSocket = viewModel.Model == e.Connection.Output
                ? e.Connection.Input
                : e.Connection.Output;
            AddConnectionLine(anotherSocket);
        }
    }

    private void OnDataContextAttached(SocketViewModel obj)
    {
        switch (obj)
        {
            case InputSocketViewModel:
            case OutputSocketViewModel:
                InitSocketPoint(obj);
                InitEditor(obj);
                obj.Model.Connected += OnSocketConnected;
                obj.Model.Disconnected += OnSocketDisconnected;
                UpdateSocketPosition();

                obj.State.Subscribe(OnStateChanged).DisposeWith(_disposables);
                break;
        }
    }

    private void OnStateChanged(SocketState obj)
    {
        if (_label != null)
        {
            grid.Children.Remove(_label);
            if (_editor != null)
                grid.Children.Remove(_editor);

            if (obj == SocketState.Disconnected)
            {
                grid.Children.Add(_editor ?? _label);
            }
            else
            {
                grid.Children.Add(_label);
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

    private void OnSocketPointDisconnectRequested(object? sender, SocketConnectRequestedEventArgs e)
    {
        if (DataContext is SocketViewModel viewModel
            && SortSocket(
                viewModel.Model, e.Target,
                out IInputSocket? inputSocket, out IOutputSocket? outputSocket))
        {
            var command = new DisconnectCommand(inputSocket, outputSocket);
            command.DoAndRecord(CommandRecorder.Default);
            e.State = SocketState.Disconnected;
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
            e.State = command.State;
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

        public SocketState State { get; set; }

        public void Do()
        {
            State = _outputSocket.TryConnect(_inputSocket)
                ? SocketState.Connected
                : SocketState.Disconnected;
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
