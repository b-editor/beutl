using Beutl.Framework;
using Beutl.NodeTree;
using Beutl.Views.NodeTree;

namespace Beutl.ViewModels.NodeTree;

public class InputSocketViewModel : SocketViewModel
{
    public InputSocketViewModel(IInputSocket socket, IPropertyEditorContext? propertyEditorContext)
        : base(socket, propertyEditorContext)
    {
        UpdateState();
    }

    public new IInputSocket Model => (IInputSocket)base.Model;

    protected override void OnSocketConnected(object? sender, SocketConnectionChangedEventArgs e)
    {
        base.OnSocketConnected(sender, e);
        UpdateState();
    }

    protected override void OnSocketDisconnected(object? sender, SocketConnectionChangedEventArgs e)
    {
        base.OnSocketDisconnected(sender, e);
        UpdateState();
    }

    private void UpdateState()
    {
        if (Model.Connection == null)
        {
            State.Value = SocketState.Disconnected;
        }
        else if (!MatchPropertyType(Model.Connection))
        {
            State.Value = SocketState.Invalid;
        }
        else
        {
            State.Value = SocketState.Connected;
        }
    }

    private static bool MatchPropertyType(IConnection connection)
    {
        IInputSocket input = connection.Input;
        IOutputSocket output = connection.Output;
        if (output.Property != null && input.Property != null)
        {
            return output.Property.Property.PropertyType.IsAssignableTo(input.Property.Property.PropertyType);
        }
        else if (output.AssociatedType != null && input.AssociatedType != null)
        {
            return output.AssociatedType.IsAssignableTo(input.AssociatedType);
        }
        else
        {
            return false;
        }
    }
}
