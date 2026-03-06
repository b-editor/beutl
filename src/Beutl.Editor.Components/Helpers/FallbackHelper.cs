using Beutl.Serialization;

namespace Beutl.Editor.Components.Helpers;

public static class FallbackHelper
{
    public static string GetTypeName(object? obj)
    {
        if ((obj as IFallback)?.TryGetTypeName(out string? result) == true)
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
