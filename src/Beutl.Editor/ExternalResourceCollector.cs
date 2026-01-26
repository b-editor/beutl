using Beutl.Engine;
using Beutl.IO;
using Beutl.Media;

namespace Beutl.Editor;

/// <summary>
/// IFileSource参照とフォント参照を収集するクラス。
/// </summary>
public sealed class ExternalResourceCollector
{
    private readonly HashSet<(Guid Object, string PropertyName, Uri OriginalUri)> _fileSources = [];
    private readonly HashSet<FontFamily> _fontFamilies = [];

    private ExternalResourceCollector()
    {
    }

    /// <summary>
    /// 収集されたファイルソースのリスト。
    /// </summary>
    public IEnumerable<(Guid Object, string PropertyName, Uri OriginalUri)> FileSources => _fileSources;

    /// <summary>
    /// 収集されたフォントファミリーのリスト。
    /// </summary>
    public IEnumerable<FontFamily> FontFamilies => _fontFamilies;

    /// <summary>
    /// 階層内のすべてのリソース参照を収集します。
    /// </summary>
    /// <param name="root">収集を開始するルート階層</param>
    /// <param name="projectDirectory">プロジェクトディレクトリのパス</param>
    /// <returns>収集されたリソース情報</returns>
    public static ExternalResourceCollector Collect(IHierarchical root, string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(projectDirectory);

        ExternalResourceCollector collector = new();

        // 階層内のすべてのEngineObjectを走査
        foreach (CoreObject obj in root.EnumerateAllChildren<CoreObject>())
        {
            collector.CollectFromObject(obj, projectDirectory);
        }

        // ルート自体がEngineObjectの場合も処理
        if (root is CoreObject rootObj)
        {
            collector.CollectFromObject(rootObj, projectDirectory);
        }

        return collector;
    }

    private void CollectFromObject(CoreObject obj, string projectDirectory)
    {
        if (obj is EngineObject engineObj)
        {
            CollectFromEngineObject(engineObj, projectDirectory);
        }

        if (obj.Uri != null && IsExternalFile(obj.Uri, projectDirectory))
        {
            _fileSources.Add((obj.Id, "Uri", obj.Uri));
        }

        var props = PropertyRegistry.GetRegistered(obj.GetType());
        foreach (var prop in props)
        {
            if (prop.PropertyType.IsValueType) continue;
            object? value = obj.GetValue(prop);
            switch (value)
            {
                case IFileSource fileSource:
                    if (fileSource.Uri != null && IsExternalFile(fileSource.Uri, projectDirectory))
                    {
                        _fileSources.Add((obj.Id, prop.Name, fileSource.Uri));
                    }

                    break;
                case FontFamily fontFamily:
                    _fontFamilies.Add(fontFamily);
                    break;
            }
        }
    }

    private void CollectFromEngineObject(EngineObject obj, string projectDirectory)
    {
        foreach (IProperty property in obj.Properties)
        {
            switch (property.CurrentValue)
            {
                // IFileSourceの収集
                case IFileSource fileSource when fileSource.Uri != null:
                    if (IsExternalFile(fileSource.Uri, projectDirectory))
                    {
                        _fileSources.Add((obj.Id, property.Name, fileSource.Uri));
                    }

                    break;
                // FontFamilyの収集
                case FontFamily fontFamily:
                    _fontFamilies.Add(fontFamily);
                    break;
            }
        }
    }

    /// <summary>
    /// URIがプロジェクト外のファイルを指しているかどうかを判定します。
    /// </summary>
    private static bool IsExternalFile(Uri uri, string projectDirectory)
    {
        if (!uri.IsFile)
            return false;

        string filePath = uri.LocalPath;
        string fullProjectPath = Path.GetFullPath(projectDirectory);

        // プロジェクトディレクトリ外のファイルは外部ファイルとみなす
        return !Path.GetFullPath(filePath).StartsWith(fullProjectPath, StringComparison.OrdinalIgnoreCase);
    }
}
