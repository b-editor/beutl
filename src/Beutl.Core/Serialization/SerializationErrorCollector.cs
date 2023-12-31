namespace Beutl.Serialization;

public class SerializationErrorCollector : ISerializationErrorNotifier
{
    public List<SerializationError> Errors { get; } = [];

    public void NotifyError(string path, string message, Exception? ex = null)
    {
        Errors.Add(new SerializationError(path, message, ex));
    }
}
