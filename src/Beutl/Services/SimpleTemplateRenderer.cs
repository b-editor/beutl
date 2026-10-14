using System.Reflection;
using System.Text.RegularExpressions;

namespace Beutl.Services;

// スクリプトのローカライズに使用
// リフレクションのキャッシュがないため、パフォーマンスはあまりよくない
public class SimpleTemplateRenderer(string template, Type[] types)
{
    public string Render()
    {
        return Regex.Replace(template, @"@\(([^)]+)\)", match =>
        {
            string expression = match.Groups[1].Value;
            string[] parts = expression.Split('.');
            if (parts.Length != 2)
            {
                throw new FormatException($"Template expression must be in the format 'TypeName.PropertyName': {expression}");
            }

            string typeName = parts[0];
            string propertyName = parts[1];

            Type? targetType = types.FirstOrDefault(t => t.Name == typeName);
            if (targetType == null)
            {
                throw new InvalidOperationException($"Type {typeName} not found.");
            }

            PropertyInfo? propInfo = targetType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
            if (propInfo == null)
            {
                throw new InvalidOperationException($"Property {propertyName} not found in type {typeName}.");
            }

            object? value = propInfo.GetValue(null);
            return value?.ToString() ?? string.Empty;
        });
    }
}
