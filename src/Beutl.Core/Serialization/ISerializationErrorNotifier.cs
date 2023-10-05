namespace Beutl.Serialization;

public interface ISerializationErrorNotifier
{
    void NotifyError(string path, string message, Exception? ex = null);
}
