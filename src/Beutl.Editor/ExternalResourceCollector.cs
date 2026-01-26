using Beutl.Engine;
using Beutl.IO;
using Beutl.Media;

namespace Beutl.Editor;

/// <summary>
/// IFileSource参照とフォント参照を収集するクラス。
/// </summary>
public sealed class ExternalResourceCollector
{
    private readonly List<(IFileSource Source, Uri OriginalUri)> _fileSources = [];
    private readonly List<FontFamily> _fontFamilies = [];

    private ExternalResourceCollector()
    {
    }

    /// <summary>
    /// 収集されたファイルソースのリスト。
    /// </summary>
    public IReadOnlyList<(IFileSource Source, Uri OriginalUri)> FileSources => _fileSources;

    /// <summary>
    /// 収集されたフォントファミリーのリスト。
    /// </summary>
    public IReadOnlyList<FontFamily> FontFamilies => _fontFamilies;

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
        HashSet<Uri> processedUris = [];
        HashSet<string> processedFonts = [];

        // 階層内のすべてのEngineObjectを走査
        foreach (EngineObject obj in root.EnumerateAllChildren<EngineObject>())
        {
            collector.CollectFromObject(obj, projectDirectory, processedUris, processedFonts);
        }

        // ルート自体がEngineObjectの場合も処理
        if (root is EngineObject rootObj)
        {
            collector.CollectFromObject(rootObj, projectDirectory, processedUris, processedFonts);
        }

        return collector;
    }

    private void CollectFromObject(EngineObject obj, string projectDirectory, HashSet<Uri> processedUris, HashSet<string> processedFonts)
    {
        foreach (IProperty property in obj.Properties)
        {
            // IFileSourceの収集
            if (typeof(IFileSource).IsAssignableFrom(property.ValueType))
            {
                if (property.CurrentValue is IFileSource fileSource && fileSource.Uri != null)
                {
                    if (IsExternalFile(fileSource.Uri, projectDirectory) && processedUris.Add(fileSource.Uri))
                    {
                        _fileSources.Add((fileSource, fileSource.Uri));
                    }
                }
            }
            // FontFamilyの収集
            else if (property.ValueType == typeof(FontFamily) || (Nullable.GetUnderlyingType(property.ValueType) == typeof(FontFamily)))
            {
                if (property.CurrentValue is FontFamily fontFamily && fontFamily.Name != null)
                {
                    if (processedFonts.Add(fontFamily.Name))
                    {
                        _fontFamilies.Add(fontFamily);
                    }
                }
            }
            // ネストされたオブジェクトの処理
            else if (property.CurrentValue is IHierarchical hierarchical)
            {
                foreach (EngineObject child in hierarchical.EnumerateAllChildren<EngineObject>())
                {
                    CollectFromObject(child, projectDirectory, processedUris, processedFonts);
                }
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
