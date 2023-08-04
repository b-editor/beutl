using System.Collections;

namespace Beutl.Media;

public class RenderInvalidatedEventArgs : EventArgs
{
    public RenderInvalidatedEventArgs(object? obj)
    {
        Sender = obj;
        Reason = RenderInvalidatedReason.None;
    }

    public RenderInvalidatedEventArgs(ICollection collection)
    {
        Sender = collection;
        Reason = RenderInvalidatedReason.CollectionChanged;
    }

    public RenderInvalidatedEventArgs(object? sender, string propertyName)
    {
        Sender = sender;
        PropertyName = propertyName;
        Reason = RenderInvalidatedReason.PropertyChanged;
    }

    public object? Sender { get; }

    public string? PropertyName { get; }

    public RenderInvalidatedReason Reason { get; }
}
