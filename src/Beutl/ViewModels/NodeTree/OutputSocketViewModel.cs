using Beutl.Framework;
using Beutl.NodeTree;
using Beutl.Views.NodeTree;

namespace Beutl.ViewModels.NodeTree;

public class OutputSocketViewModel : SocketViewModel
{
    public OutputSocketViewModel(IOutputSocket socket, IPropertyEditorContext? propertyEditorContext)
        : base(socket, propertyEditorContext)
    {
        UpdateState();
    }

    public new IOutputSocket Model => (IOutputSocket)base.Model;

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
        if (Model.Connections.Count == 0)
        {
            State.Value = SocketState.Disconnected;
        }
        else if (Model.Connections.Any(x => !MatchPropertyType(x.Input, x.Output)))
        {
            State.Value = SocketState.Invalid;
        }
        else
        {
            State.Value = SocketState.Connected;
        }
    }

    private static bool MatchPropertyType(IInputSocket input, IOutputSocket output)
    {
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
