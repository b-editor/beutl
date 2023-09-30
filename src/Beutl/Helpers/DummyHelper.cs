using Beutl.Serialization;

namespace Beutl.Helpers;

public static class DummyHelper
{
    public static string GetTypeName(object? obj)
    {
        if ((obj as IDummy)?.TryGetTypeName(out string? result) == true)
        {
            return result;
        }
        else if (obj != null)
        {
            return TypeFormat.ToString(obj.GetType());
        }
        else
        {
            return Strings.Unknown;
        }
    }
}
