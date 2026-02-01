using System.Diagnostics.CodeAnalysis;
using Beutl.Configuration;
using Beutl.Engine;
using Beutl.IO;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Editor;

/// <summary>
/// リソースファイルのコピーとUri書き換えを行うサービス。
/// </summary>
public sealed class ResourceRelocationService
{
    private readonly ILogger _logger = Log.CreateLogger<ResourceRelocationService>();
    private readonly Func<string, IEnumerable<string>>? _fontFileFinder;

    /// <summary>
    /// デフォルトのコンストラクタ。
    /// </summary>
    public ResourceRelocationService()
    {
    }

    /// <summary>
    /// テスト用コンストラクタ。フォントファイル検索ロジックをカスタマイズできます。
    /// </summary>
    /// <param name="fontFileFinder">フォントファイル検索関数</param>
    internal ResourceRelocationService(Func<string, IEnumerable<string>> fontFileFinder)
    {
        _fontFileFinder = fontFileFinder;
    }

    /// <summary>
    /// ファイルソースをプロジェクトのresourcesディレクトリにコピーし、URIを更新します。
    /// </summary>
    /// <param name="sources">コピーするファイルソースのリスト</param>
    /// <param name="stagingProject">URIの更新を適用するプロジェクト</param>
    /// <param name="projectDirectory">プロジェクトディレクトリのパス</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>コピーされたファイルの数</returns>
    public async Task<int> RelocateFileSourcesAsync(
        IEnumerable<(Guid Object, string PropertyName, Uri OriginalUri)> sources,
        Project stagingProject,
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        string resourcesDir = Path.Combine(projectDirectory, "resources");
        Directory.CreateDirectory(resourcesDir);

        int count = 0;
        foreach (var group in sources.GroupBy(i => i.OriginalUri))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var originalUri = group.Key;
            try
            {
                string sourceFilePath = originalUri.LocalPath;
                if (!File.Exists(sourceFilePath))
                {
                    _logger.LogWarning("Source file not found: {FilePath}", sourceFilePath);
                    continue;
                }

                string fileName = Path.GetFileName(sourceFilePath);
                string destFilePath = GetUniqueFilePath(resourcesDir, fileName);

                await CopyFileAsync(sourceFilePath, destFilePath, cancellationToken);

                foreach ((Guid id, string prop, _) in group)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // URIを新しいパスに更新
                    UpdateUri(stagingProject, id, prop, new Uri(destFilePath));

                    count++;
                    _logger.LogDebug("Relocated file: {OriginalPath} -> {NewPath}", sourceFilePath, destFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to relocate file: {Uri}", originalUri);
            }
        }

        return count;
    }

    private void UpdateUri(Project stagingProject, Guid id, string propertyName, Uri newUri)
    {
        var obj = (CoreObject?)stagingProject.FindById(id);
        if (obj is EngineObject engineObject)
        {
            var engineProp = engineObject.Properties.FirstOrDefault(p => p.Name == propertyName);
            if (engineProp?.CurrentValue is IFileSource fileSource)
            {
                var type = fileSource.GetType();
                var newInstance = (IFileSource)Activator.CreateInstance(type)!;
                newInstance.ReadFrom(newUri);
                engineProp.CurrentValue = newInstance;
                return;
            }
        }

        if (obj != null)
        {
            var property = PropertyRegistry.FindRegistered(obj, propertyName);
            if (property != null && property.PropertyType == typeof(Uri))
            {
                obj.SetValue(property, newUri);
                return;
            }

            if (propertyName == "Uri")
            {
                obj.Uri = newUri;
                return;
            }
        }

        throw new InvalidOperationException("Failed to update URI: Object or property not found.");
    }

    /// <summary>
    /// フォントファイルをプロジェクトのresources/fontsディレクトリにコピーします。
    /// </summary>
    /// <param name="fontFamilies">コピーするフォントファミリーのリスト</param>
    /// <param name="projectDirectory">プロジェクトディレクトリのパス</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>コピーされたフォントファイルの数</returns>
    public async Task<int> RelocateFontsAsync(
        IEnumerable<FontFamily> fontFamilies,
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        string fontsDir = Path.Combine(projectDirectory, "resources", "fonts");
        Directory.CreateDirectory(fontsDir);

        HashSet<string> copiedFiles = [];
        int count = 0;

        foreach (FontFamily fontFamily in fontFamilies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                IEnumerable<string> fontFiles = _fontFileFinder != null
                    ? _fontFileFinder(fontFamily.Name)
                    : FindFontFiles(fontFamily.Name);
                foreach (string sourceFilePath in fontFiles)
                {
                    if (!copiedFiles.Add(sourceFilePath))
                        continue;

                    string fileName = Path.GetFileName(sourceFilePath);
                    string destFilePath = GetUniqueFilePath(fontsDir, fileName);

                    await CopyFileAsync(sourceFilePath, destFilePath, cancellationToken);

                    count++;
                    _logger.LogDebug("Relocated font: {OriginalPath} -> {NewPath}", sourceFilePath, destFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to relocate font: {FontFamily}", fontFamily.Name);
            }
        }

        return count;
    }

    /// <summary>
    /// フォントファミリー名に一致するフォントファイルを検索します。
    /// </summary>
    /// <remarks>
    /// このメソッドはOS依存の処理や外部依存（SKTypeface、GlobalConfiguration）を含むため、
    /// テスト時は<see cref="_fontFileFinder"/>を使用してバイパスされます。
    /// </remarks>
    [ExcludeFromCodeCoverage]
    private static IEnumerable<string> FindFontFiles(string fontFamilyName)
    {
        IReadOnlyList<string> fontDirs = GlobalConfiguration.Instance.FontConfig.FontDirectories;
        List<string> foundFiles = [];

        foreach (string fontDir in fontDirs)
        {
            if (!Directory.Exists(fontDir))
                continue;

            foreach (string file in Directory.GetFiles(fontDir, "*.*", SearchOption.AllDirectories))
            {
                ReadOnlySpan<char> ext = Path.GetExtension(file.AsSpan());
                if (!ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".ttc", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".otf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    using SKTypeface? typeface = SKTypeface.FromFile(file);
                    if (typeface != null &&
                        string.Equals(typeface.FamilyName, fontFamilyName, StringComparison.OrdinalIgnoreCase))
                    {
                        foundFiles.Add(file);
                    }
                }
                catch
                {
                    // フォントファイルの読み込みに失敗した場合はスキップ
                }
            }
        }

        // システムフォントも検索（プラットフォーム固有のパス）
        string[] systemFontDirs = GetSystemFontDirectories();
        foreach (string fontDir in systemFontDirs)
        {
            if (!Directory.Exists(fontDir))
                continue;

            try
            {
                foreach (string file in Directory.GetFiles(fontDir, "*.*", SearchOption.AllDirectories))
                {
                    ReadOnlySpan<char> ext = Path.GetExtension(file.AsSpan());
                    if (!ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".ttc", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".otf", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        using SKTypeface? typeface = SKTypeface.FromFile(file);
                        if (typeface != null &&
                            string.Equals(typeface.FamilyName, fontFamilyName, StringComparison.OrdinalIgnoreCase))
                        {
                            foundFiles.Add(file);
                        }
                    }
                    catch
                    {
                        // フォントファイルの読み込みに失敗した場合はスキップ
                    }
                }
            }
            catch
            {
                // ディレクトリのアクセスに失敗した場合はスキップ
            }
        }

        return foundFiles;
    }

    /// <summary>
    /// システムフォントディレクトリを取得します。
    /// </summary>
    /// <remarks>
    /// このメソッドはOS依存の分岐を含み、現在のOS以外のパスはテストできないため、
    /// カバレッジ計測から除外されています。
    /// </remarks>
    [ExcludeFromCodeCoverage]
    private static string[] GetSystemFontDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            return
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts)),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft",
                    "Windows", "Fonts")
            ];
        }
        else if (OperatingSystem.IsMacOS())
        {
            return
            [
                "/System/Library/Fonts",
                "/Library/Fonts",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Fonts")
            ];
        }
        else if (OperatingSystem.IsLinux())
        {
            return
            [
                "/usr/share/fonts",
                "/usr/local/share/fonts",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/fonts")
            ];
        }

        return [];
    }

    /// <summary>
    /// 重複しないファイルパスを取得します。
    /// </summary>
    private static string GetUniqueFilePath(string directory, string fileName)
    {
        string destFilePath = Path.Combine(directory, fileName);
        if (!File.Exists(destFilePath))
            return destFilePath;

        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        int counter = 1;

        while (File.Exists(destFilePath))
        {
            destFilePath = Path.Combine(directory, $"{fileNameWithoutExt}_{counter}{ext}");
            counter++;
        }

        return destFilePath;
    }

    /// <summary>
    /// 非同期でファイルをコピーします。
    /// </summary>
    private static async Task CopyFileAsync(string sourcePath, string destPath, CancellationToken cancellationToken)
    {
        await using FileStream sourceStream = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream destStream = new(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await sourceStream.CopyToAsync(destStream, cancellationToken);
    }
}
