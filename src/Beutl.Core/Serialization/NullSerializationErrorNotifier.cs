namespace Beutl.Serialization;

public class NullSerializationErrorNotifier : ISerializationErrorNotifier
{
    public static readonly NullSerializationErrorNotifier Instance = new();

    public void NotifyError(string path, string message, Exception? ex = null)
    {
    }
}
