using System.IO.Compression;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor;

/// <summary>
/// プロジェクトのエクスポート/インポートを行うサービス。
/// </summary>
public sealed class ProjectPackageService
{
    public static ProjectPackageService Current { get; } = new();

    private readonly ILogger _logger = Log.CreateLogger<ProjectPackageService>();
    private readonly ResourceRelocationService _relocationService = new();

    private ProjectPackageService()
    {
    }

    /// <summary>
    /// プロジェクトをZIPパッケージとしてエクスポートします。
    /// </summary>
    /// <param name="project">エクスポートするプロジェクト</param>
    /// <param name="outputPath">出力先のZIPファイルパス</param>
    /// <param name="progress">進捗報告</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>エクスポートが成功した場合はtrue</returns>
    public async Task<bool> ExportAsync(
        Project project,
        string outputPath,
        IProgress<(string Message, double Progress)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(outputPath);

        if (project.Uri == null)
        {
            throw new InvalidOperationException("Project must be saved before exporting.");
        }

        string? tempDir = null;

        try
        {
            // Step 1: 一時ディレクトリを作成
            progress?.Report((Strings.ExportingProject, 0.0));
            tempDir = Path.Combine(Path.GetTempPath(), $"beutl_export_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            string projectDir = Path.GetDirectoryName(project.Uri.LocalPath)!;
            string tempProjectDir = Path.Combine(tempDir, Path.GetFileName(projectDir));

            // Step 2: プロジェクトディレクトリをコピー
            progress?.Report((Strings.ExportingProject, 0.1));
            await CopyDirectoryAsync(projectDir, tempProjectDir, cancellationToken);

            // Step 3: コピーしたプロジェクトを開く
            progress?.Report((Strings.ExportingProject, 0.2));
            string tempProjectFile = Path.Combine(tempProjectDir, Path.GetFileName(project.Uri.LocalPath));
            Uri tempProjectUri = new(tempProjectFile);
            Project tempProject = CoreSerializer.RestoreFromUri<Project>(tempProjectUri);

            // Step 4: 仮想ルートにアタッチ
            progress?.Report((Strings.ExportingProject, 0.3));
            VirtualProjectRoot virtualRoot = new();
            virtualRoot.AttachProject(tempProject);

            // Step 5-6: 外部ファイルを収集してコピー
            progress?.Report((Strings.ExportingProject, 0.4));
            ExternalResourceCollector collector = ExternalResourceCollector.Collect(virtualRoot, tempProjectDir);

            int fileCount = await _relocationService.RelocateFileSourcesAsync(
                collector.FileSources,
                tempProjectDir,
                cancellationToken);
            _logger.LogInformation("Relocated {Count} external files", fileCount);

            // Step 7: フォントをコピー
            progress?.Report((Strings.ExportingProject, 0.6));
            int fontCount = await _relocationService.RelocateFontsAsync(
                collector.FontFamilies,
                tempProjectDir,
                cancellationToken);
            _logger.LogInformation("Relocated {Count} font files", fontCount);

            // Step 8: プロジェクトを保存
            progress?.Report((Strings.ExportingProject, 0.8));
            CoreSerializer.StoreToUri(tempProject, tempProjectUri);

            // アイテムも保存
            foreach (ProjectItem item in tempProject.Items)
            {
                if (item.Uri != null)
                {
                    CoreSerializer.StoreToUri(item, item.Uri);
                }
            }

            // 仮想ルートからデタッチ
            virtualRoot.DetachProject();

            // Step 9: ZIPファイルを作成
            progress?.Report((Strings.ExportingProject, 0.9));
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            await Task.Run(() => ZipFile.CreateFromDirectory(tempProjectDir, outputPath), cancellationToken);

            progress?.Report((Strings.ExportingProject, 1.0));
            _logger.LogInformation("Project exported successfully to {OutputPath}", outputPath);

            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Export operation was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export project");
            return false;
        }
        finally
        {
            // 一時ディレクトリをクリーンアップ
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup temp directory: {TempDir}", tempDir);
                }
            }
        }
    }

    /// <summary>
    /// ZIPパッケージからプロジェクトをインポートします。
    /// </summary>
    /// <param name="packagePath">インポートするZIPファイルのパス</param>
    /// <param name="destinationDirectory">展開先のディレクトリ</param>
    /// <param name="progress">進捗報告</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>インポートされたプロジェクト、失敗した場合はnull</returns>
    public async Task<Project?> ImportAsync(
        string packagePath,
        string destinationDirectory,
        IProgress<(string Message, double Progress)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packagePath);
        ArgumentNullException.ThrowIfNull(destinationDirectory);

        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("Package file not found.", packagePath);
        }

        try
        {
            progress?.Report((Strings.ImportingProject, 0.0));

            // パッケージ名からプロジェクトディレクトリ名を取得
            string packageName = Path.GetFileNameWithoutExtension(packagePath);
            string projectDir = GetUniqueDirectoryPath(destinationDirectory, packageName);

            // ZIPを展開
            progress?.Report((Strings.ImportingProject, 0.3));
            await Task.Run(() => ZipFile.ExtractToDirectory(packagePath, projectDir), cancellationToken);

            // プロジェクトファイルを検索
            progress?.Report((Strings.ImportingProject, 0.6));
            string? projectFile = Directory.GetFiles(projectDir, "*.bep", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();

            if (projectFile == null)
            {
                _logger.LogError("No project file found in package");
                Directory.Delete(projectDir, recursive: true);
                return null;
            }

            // プロジェクトを開く
            progress?.Report((Strings.ImportingProject, 0.8));
            Uri projectUri = new(projectFile);
            Project project = CoreSerializer.RestoreFromUri<Project>(projectUri);

            progress?.Report((Strings.ImportingProject, 1.0));
            _logger.LogInformation("Project imported successfully from {PackagePath} to {ProjectDir}", packagePath, projectDir);

            return project;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Import operation was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import project");
            return null;
        }
    }

    /// <summary>
    /// 重複しないディレクトリパスを取得します。
    /// </summary>
    private static string GetUniqueDirectoryPath(string parentDirectory, string directoryName)
    {
        string path = Path.Combine(parentDirectory, directoryName);
        if (!Directory.Exists(path))
            return path;

        int counter = 1;
        while (Directory.Exists(path))
        {
            path = Path.Combine(parentDirectory, $"{directoryName}_{counter}");
            counter++;
        }

        return path;
    }

    /// <summary>
    /// ディレクトリを非同期でコピーします。
    /// </summary>
    private static async Task CopyDirectoryAsync(string sourceDir, string destDir, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            await CopyFileAsync(file, destFile, cancellationToken);
        }

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dirName = Path.GetFileName(subDir);

            // .beutlフォルダはスキップ（ビュー状態など）
            if (dirName == ".beutl")
                continue;

            string destSubDir = Path.Combine(destDir, dirName);
            await CopyDirectoryAsync(subDir, destSubDir, cancellationToken);
        }
    }

    /// <summary>
    /// ファイルを非同期でコピーします。
    /// </summary>
    private static async Task CopyFileAsync(string sourcePath, string destPath, CancellationToken cancellationToken)
    {
        await using FileStream sourceStream = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream destStream = new(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await sourceStream.CopyToAsync(destStream, cancellationToken);
    }
}
