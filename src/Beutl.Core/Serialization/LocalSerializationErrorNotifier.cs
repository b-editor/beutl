namespace Beutl.Serialization;

internal static class LocalSerializationErrorNotifier
{
    [ThreadStatic]
    public static ISerializationErrorNotifier? Current;
}
