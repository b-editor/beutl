namespace Beutl.Editor;

public class PropertyPathHelper
{
    // Pathから末端のプロパティ名を取得する
    public static string GetPropertyNameFromPath(string propertyPath)
    {
        string[] parts = propertyPath.Split('.');
        return parts[^1];
    }
}
