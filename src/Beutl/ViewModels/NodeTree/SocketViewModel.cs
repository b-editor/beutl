using System.Diagnostics.CodeAnalysis;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using Beutl.Commands;
using Beutl.Framework;
using Beutl.NodeTree;

using Reactive.Bindings;

namespace Beutl.ViewModels.NodeTree;

public class SocketViewModel : NodeItemViewModel
{
    private readonly IDisposable? _disposable;

    public SocketViewModel(ISocket? socket, IPropertyEditorContext? propertyEditorContext, Node node)
        : base(socket, propertyEditorContext, node)
    {
        _disposable = (socket as NodeItem)?.GetObservable(NodeItem.IsValidProperty)?.Subscribe(OnIsConnectedChanged);
        if (socket != null)
        {
            Brush = new(new ImmutableSolidColorBrush(socket.Color.ToAvalonia()));
            socket.Connected += OnSocketConnected;
            socket.Disconnected += OnSocketDisconnected;
        }
        else
        {
            Brush = new(Brushes.Gray);
        }
    }

    public new ISocket? Model => base.Model as ISocket;

    // IsValidがfalseの時、false
    public ReactivePropertySlim<bool> IsConnected { get; } = new();

    public ReactivePropertySlim<IBrush> Brush { get; }

    public ReactivePropertySlim<Point> SocketPosition { get; } = new();

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

    public bool TryConnect(SocketViewModel target)
    {
        if (target.Model == null ^ Model == null)
        {
            // どちらかがNull
            ISocketsCanBeAdded? groupNode = null;
            ISocket? socket = null;
            switch ((Node, target.Node))
            {
                case (ISocketsCanBeAdded node1, _):
                    groupNode = node1;
                    socket = target.Model;
                    break;

                case (_, ISocketsCanBeAdded node2):
                    groupNode = node2;
                    socket = Model;
                    break;
            }

            if (groupNode != null && socket != null
                && groupNode.AddSocket(socket, out Connection? connection))
            {
                var command = new ConnectGeneratedSocketCommand(connection, groupNode);
                CommandRecorder.Default.PushOnly(command);
            }

            return false;
        }
        else if (Model != null && target.Model != null
            && SortSocket(Model, target.Model, out IInputSocket? inputSocket, out IOutputSocket? outputSocket))
        {
            var command = new ConnectCommand(inputSocket, outputSocket);
            command.DoAndRecord(CommandRecorder.Default);
            return command.IsConnected;
        }
        else
        {
            return false;
        }
    }

    public bool TryDisconnect(SocketViewModel target)
    {
        if (Model != null && target.Model != null
            && SortSocket(Model, target.Model, out IInputSocket? inputSocket, out IOutputSocket? outputSocket))
        {
            var command = new DisconnectCommand(inputSocket, outputSocket);
            command.DoAndRecord(CommandRecorder.Default);
            return true;
        }
        else
        {
            return false;
        }
    }

    public void DisconnectAll()
    {
        switch (Model)
        {
            case IInputSocket inputSocket when inputSocket.Connection is { } connection:
                new DisconnectCommand(connection.Input, connection.Output)
                    .DoAndRecord(CommandRecorder.Default);
                break;
            case IOutputSocket outputSocket when outputSocket.Connections.Count > 0:
                outputSocket.Connections.Select(x => new DisconnectCommand(x.Input, x.Output))
                    .ToArray()
                    .ToCommand()
                    .DoAndRecord(CommandRecorder.Default);
                break;
        }
    }

    public void Remove()
    {
        if (Model is IAutomaticallyGeneratedSocket generatedSocket)
        {
            Node.Items.BeginRecord<INodeItem>()
                .Remove(generatedSocket)
                .ToCommand()
                .DoAndRecord(CommandRecorder.Default);
        }
    }

    public void UpdateName(string? e)
    {
        new ChangePropertyCommand<string>((ICoreObject)Model!, CoreObject.NameProperty, e, Model!.Name)
            .DoAndRecord(CommandRecorder.Default);
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        _disposable?.Dispose();
        if (Model != null)
        {
            Model.Connected -= OnSocketConnected;
            Model.Disconnected -= OnSocketDisconnected;
        }
    }

    protected virtual void OnSocketDisconnected(object? sender, SocketConnectionChangedEventArgs e)
    {
        if (Model != null)
        {
            OnIsConnectedChanged(((NodeItem)Model).IsValid);
        }
    }

    protected virtual void OnSocketConnected(object? sender, SocketConnectionChangedEventArgs e)
    {
        if (Model != null)
        {
            OnIsConnectedChanged(((NodeItem)Model).IsValid);
        }
    }

    protected virtual void OnIsConnectedChanged(bool? isValid)
    {
    }

    private sealed class ConnectGeneratedSocketCommand : IRecordableCommand
    {
        private readonly ISocketsCanBeAdded _node;
        private readonly ISocket _socket;
        private readonly int _index = -1;

        public ConnectGeneratedSocketCommand(Connection connection, ISocketsCanBeAdded node)
        {
            _node = node;
            if (node is Node node1)
            {
                _index = node1.Items.IndexOf(connection.Input);
                _socket = connection.Input;
                if (_index < 0)
                {
                    _index = node1.Items.IndexOf(connection.Output);
                    _socket = connection.Output;
                }
            }

            if (_index < 0 || _socket == null)
            {
                throw new Exception();
            }
        }

        public void Do()
        {
            // 実行されない。
        }

        public void Redo()
        {
            if (_node is Node node1)
            {
                node1.Items.Insert(_index, _socket);
            }
        }

        public void Undo()
        {
            if (_node is Node node1)
            {
                node1.Items.Remove(_socket);
            }
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
