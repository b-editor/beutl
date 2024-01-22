using Beutl.NodeTree;

using Reactive.Bindings;

namespace Beutl.ViewModels.NodeTree;

public class InputSocketViewModel : SocketViewModel
{
    private readonly ReactivePropertySlim<IObservable<ConnectionStatus>?> _statusSource = new();

    public InputSocketViewModel(IInputSocket? socket, IPropertyEditorContext? propertyEditorContext, Node node, EditViewModel editViewModel)
        : base(socket, propertyEditorContext, node, editViewModel)
    {
        Status = _statusSource
            .Select(o => o ?? Observable.Return(ConnectionStatus.Disconnected))
            .Switch()
            .ToReadOnlyReactivePropertySlim();
    }

    public new IInputSocket? Model => base.Model as IInputSocket;

    public IReadOnlyReactiveProperty<ConnectionStatus> Status { get; }

    protected override void OnDispose()
    {
        base.OnDispose();
        _statusSource.Dispose();
        Status.Dispose();
    }

    protected override void OnIsConnectedChanged()
    {
        if (Model != null)
        {
            IsConnected.Value = Model.Connection != null;
            _statusSource.Value = Model.Connection?.GetObservable(Connection.StatusProperty);
        }
    }
}
