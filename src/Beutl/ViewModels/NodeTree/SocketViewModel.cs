using System.Diagnostics.CodeAnalysis;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.NodeTree;

using Reactive.Bindings;

namespace Beutl.ViewModels.NodeTree;

public class SocketViewModel : NodeItemViewModel
{
    private readonly EditViewModel _editViewModel;

    public SocketViewModel(ISocket? socket, IPropertyEditorContext? propertyEditorContext, Node node, EditViewModel editViewModel)
        : base(socket, propertyEditorContext, node)
    {
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

        OnIsConnectedChanged();
        _editViewModel = editViewModel;
    }

    public new ISocket? Model => base.Model as ISocket;

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
                && groupNode.AddSocket(socket, out _))
            {
                _editViewModel.HistoryManager.Commit(CommandNames.AddSocket);
            }

            return false;
        }
        else if (Model != null && target.Model != null
            && SortSocket(Model, target.Model, out IInputSocket? inputSocket, out IOutputSocket? outputSocket))
        {
            bool isConnected = outputSocket.TryConnect(inputSocket);
            _editViewModel.HistoryManager.Commit(CommandNames.ConnectSocket);

            return isConnected;
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
            outputSocket.Disconnect(inputSocket);
            _editViewModel.HistoryManager.Commit(CommandNames.DisconnectSocket);

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
                connection.Output.Disconnect(connection.Input);
                _editViewModel.HistoryManager.Commit(CommandNames.DisconnectSocket);
                break;

            case IOutputSocket outputSocket when outputSocket.Connections.Count > 0:
                // Disconnect内でConnectionsから要素を削除するのでToArrayする必要がある
                foreach (Connection connection in outputSocket.Connections.ToArray())
                {
                    connection.Output.Disconnect(connection.Input);
                }
                _editViewModel.HistoryManager.Commit(CommandNames.DisconnectSocket);
                break;
        }
    }

    public void Remove()
    {
        if (Model is IAutomaticallyGeneratedSocket generatedSocket)
        {
            Node.Items.Remove(generatedSocket);
            _editViewModel.HistoryManager.Commit(CommandNames.RemoveSocket);
        }
    }

    public void UpdateName(string? e)
    {
        Model!.Name = e!;
        _editViewModel.HistoryManager.Commit(CommandNames.RenameSocket);
    }

    protected override void OnDispose()
    {
        base.OnDispose();
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
            OnIsConnectedChanged();
        }
    }

    protected virtual void OnSocketConnected(object? sender, SocketConnectionChangedEventArgs e)
    {
        if (Model != null)
        {
            OnIsConnectedChanged();
        }
    }

    protected virtual void OnIsConnectedChanged()
    {
    }
}
