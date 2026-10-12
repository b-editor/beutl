namespace Beutl.Editor.Infrastructure;

public class PropertyPathHelper
{
    public static string GetPropertyNameFromPath(string propertyPath)
    {
        string[] parts = propertyPath.Split('.');
        return parts[^1];
    }
}
