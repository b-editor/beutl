namespace Beutl.Serialization;

public record SerializationError(string Path, string Message, Exception? Exception = null);
