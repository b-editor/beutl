using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor;

/// <summary>
/// Result of a project export operation.
/// </summary>
/// <param name="Success">Whether the ZIP was written. Cancellation does not set this to <c>false</c> — it propagates as <see cref="OperationCanceledException"/>.</param>
/// <param name="FailedResources">Identifiers of resources that could not be fully relocated. Non-empty while <see cref="Success"/> is <c>true</c> means partial failure: the ZIP exists, but some referenced files/fonts are either missing from it or still pointing at the original path inside the saved project. When <see cref="Success"/> is <c>false</c>, this preserves any failures that were already collected before the export was aborted.</param>
public sealed record ExportResult(bool Success, IReadOnlyList<string> FailedResources);

/// <summary>
/// Service for exporting and importing projects.
/// </summary>
public sealed class ProjectPackageService
{
    public static ProjectPackageService Current { get; } = new();

    private readonly ILogger _logger = Log.CreateLogger<ProjectPackageService>();
    private readonly ResourceRelocationService _relocationService;

    private ProjectPackageService()
    {
        _relocationService = new ResourceRelocationService();
    }

    internal ProjectPackageService(ResourceRelocationService relocationService)
    {
        _relocationService = relocationService;
    }

    /// <summary>
    /// Exports a project as a ZIP package.
    /// </summary>
    /// <param name="project">The project to export.</param>
    /// <param name="outputPath">The output ZIP file path.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="ExportResult"/> describing whether the export completed and any resources that failed to copy.</returns>
    public async Task<ExportResult> ExportAsync(
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
        RelocationResult fileResult = new(0, []);
        RelocationResult fontResult = new(0, []);

        try
        {
            // Step 1: Create a temporary directory
            progress?.Report((Strings.ExportingProject, 0.0));
            tempDir = Path.Combine(Path.GetTempPath(), $"beutl_export_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            // Step 2: Copy the project directory
            string projectDir = Path.GetDirectoryName(project.Uri.LocalPath)!;
            string tempProjectDir = Path.Combine(tempDir, Path.GetFileName(projectDir));
            progress?.Report((Strings.ExportingProject, 0.1));
            await CopyDirectoryAsync(projectDir, tempProjectDir, cancellationToken);

            // Step 3: Open the temporary project
            string tempProjectFile = Path.Combine(tempProjectDir, Path.GetFileName(project.Uri.LocalPath));
            Uri tempProjectUri = new(tempProjectFile);
            progress?.Report((Strings.ExportingProject, 0.2));
            Project tempProject = CoreSerializer.RestoreFromUri<Project>(tempProjectUri);

            // Step 4: Attach to the virtual root
            progress?.Report((Strings.ExportingProject, 0.3));
            VirtualProjectRoot virtualRoot = new();
            virtualRoot.AttachProject(tempProject);

            // Step 5: Collect and copy external files
            progress?.Report((Strings.ExportingProject, 0.4));
            ExternalResourceCollector collector = ExternalResourceCollector.Collect(project, projectDir);

            fileResult = await _relocationService.RelocateFileSourcesAsync(
                collector.FileSources,
                tempProject,
                tempProjectDir,
                cancellationToken);
            _logger.LogInformation("Relocated {Count} external files", fileResult.SuccessCount);
            if (fileResult.FailedResources.Count > 0)
            {
                _logger.LogWarning(
                    "Failed to relocate {Count} external files: {Resources}",
                    fileResult.FailedResources.Count,
                    string.Join(", ", fileResult.FailedResources));
            }

            // Step 6: Copy fonts
            progress?.Report((Strings.ExportingProject, 0.6));
            fontResult = await _relocationService.RelocateFontsAsync(
                collector.FontFamilies,
                tempProjectDir,
                cancellationToken);
            _logger.LogInformation("Relocated {Count} font files", fontResult.SuccessCount);
            if (fontResult.FailedResources.Count > 0)
            {
                _logger.LogWarning(
                    "Failed to relocate {Count} font families: {Resources}",
                    fontResult.FailedResources.Count,
                    string.Join(", ", fontResult.FailedResources));
            }

            // Step 7: Save the project
            progress?.Report((Strings.ExportingProject, 0.8));
            CoreSerializer.StoreToUri(tempProject, tempProjectUri);

            // Detach from the virtual root
            virtualRoot.DetachProject();

            // Step 8: Create the ZIP file
            progress?.Report((Strings.ExportingProject, 0.9));
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            await Task.Run(() => ZipFile.CreateFromDirectory(tempProjectDir, outputPath), cancellationToken);

            progress?.Report((Strings.ExportingProject, 1.0));
            _logger.LogInformation("Project exported successfully to {OutputPath}", outputPath);

            List<string> failedResources = [.. fileResult.FailedResources, .. fontResult.FailedResources];
            return new ExportResult(true, failedResources);
        }
        catch (OperationCanceledException)
        {
            LogExportCancelled();
            throw;
        }
        catch (Exception ex)
        {
            return LogExportError(ex, fileResult, fontResult);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [ExcludeFromCodeCoverage]
    private void LogExportCancelled()
    {
        _logger.LogInformation("Export operation was cancelled");
    }

    private ExportResult LogExportError(Exception ex, RelocationResult fileResult, RelocationResult fontResult)
    {
        _logger.LogError(ex, "Failed to export project");
        List<string> failedResources = [.. fileResult.FailedResources, .. fontResult.FailedResources];
        return new ExportResult(false, failedResources);
    }

    [ExcludeFromCodeCoverage]
    private void CleanupTempDirectory(string? tempDir)
    {
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

    /// <summary>
    /// Imports a project from a ZIP package.
    /// </summary>
    /// <param name="packagePath">The path of the ZIP file to import.</param>
    /// <param name="destinationDirectory">The directory to extract to.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The imported project, or <c>null</c> if import failed.</returns>
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
            string packageName = Path.GetFileNameWithoutExtension(packagePath);
            string projectDir = GetUniqueDirectoryPath(destinationDirectory, packageName);

            try
            {
                progress?.Report((Strings.ImportingProject, 0.3));
                await Task.Run(() => ZipFile.ExtractToDirectory(packagePath, projectDir), cancellationToken);

                progress?.Report((Strings.ImportingProject, 0.6));
                string? projectFile = Directory.GetFiles(projectDir, "*.bep", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();

                if (projectFile == null)
                {
                    _logger.LogError("No project file found in package");
                    CleanupTempDirectory(projectDir);
                    return null;
                }

                progress?.Report((Strings.ImportingProject, 0.8));
                Uri projectUri = new(projectFile);
                Project project = CoreSerializer.RestoreFromUri<Project>(projectUri);

                progress?.Report((Strings.ImportingProject, 1.0));
                _logger.LogInformation("Project imported successfully from {PackagePath} to {ProjectDir}",
                    packagePath, projectDir);
                return project;
            }
            catch
            {
                CleanupTempDirectory(projectDir);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            LogImportCancelled();
            throw;
        }
        catch (Exception ex)
        {
            return LogImportError(ex);
        }
    }

    [ExcludeFromCodeCoverage]
    private void LogImportCancelled()
    {
        _logger.LogInformation("Import operation was cancelled");
    }

    [ExcludeFromCodeCoverage]
    private Project? LogImportError(Exception ex)
    {
        _logger.LogError(ex, "Failed to import project");
        return null;
    }

    /// <summary>
    /// Gets a unique directory path that does not conflict with existing directories.
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
    /// Copies a directory asynchronously.
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

            // Skip the .beutl folder (view state, etc.)
            if (dirName == ".beutl")
                continue;

            string destSubDir = Path.Combine(destDir, dirName);
            await CopyDirectoryAsync(subDir, destSubDir, cancellationToken);
        }
    }

    /// <summary>
    /// Copies a file asynchronously.
    /// </summary>
    private static async Task CopyFileAsync(string sourcePath, string destPath, CancellationToken cancellationToken)
    {
        await using FileStream sourceStream = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream destStream = new(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await sourceStream.CopyToAsync(destStream, cancellationToken);
    }
}
