using System.Reflection;
using System.Text.RegularExpressions;

namespace Beutl.Services;

// スクリプトのローカライズに使用
// リフレクションのキャッシュがないため、パフォーマンスはあまりよくない
public class SimpleTemplateRenderer(string template, Type[] types)
{
    public string Render()
    {
        // @( ... ) の部分を抽出し、置換する
        return Regex.Replace(template, @"@\(([^)]+)\)", match =>
        {
            // @(Strings.Greeting) などの形式を想定
            string expression = match.Groups[1].Value; // "Strings.Greeting" の部分
            string[] parts = expression.Split('.');
            if (parts.Length != 2)
            {
                throw new FormatException($"Template expression must be in the format 'TypeName.PropertyName': {expression}");
            }

            string typeName = parts[0];
            string propertyName = parts[1];

            // コンストラクタで渡された型の中から、名前が一致する型を探す
            Type? targetType = types.FirstOrDefault(t => t.Name == typeName);
            if (targetType == null)
            {
                throw new InvalidOperationException($"Type {typeName} not found.");
            }

            // 指定されたプロパティを、publicかつstaticなものとして取得
            PropertyInfo? propInfo = targetType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
            if (propInfo == null)
            {
                throw new InvalidOperationException($"Property {propertyName} not found in type {typeName}.");
            }

            // static プロパティの値を取得（インスタンスは不要なので null を指定）
            object? value = propInfo.GetValue(null);
            return value?.ToString() ?? string.Empty;
        });
    }
}
