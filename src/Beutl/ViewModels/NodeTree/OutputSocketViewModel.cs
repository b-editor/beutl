using System.Runtime.Serialization;

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
        bool MatchType()
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

        if (MatchType())
        {
            return true;
        }
        else
        {
            if (output.AssociatedType != null && input.AssociatedType != null
                && !output.AssociatedType.IsAssignableTo(typeof(IDisposable))
                && !input.AssociatedType.IsAssignableTo(typeof(IDisposable)))
            {
                try
                {
                    object dummy = FormatterServices.GetUninitializedObject(input.AssociatedType);
                    _ = Convert.ChangeType(dummy, output.AssociatedType);
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }
    }
}
