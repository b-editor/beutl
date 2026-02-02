using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor;

/// <summary>
/// Service for exporting and importing projects.
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
    /// Exports a project as a ZIP package.
    /// </summary>
    /// <param name="project">The project to export.</param>
    /// <param name="outputPath">The output ZIP file path.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the export was successful.</returns>
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

            int fileCount = await _relocationService.RelocateFileSourcesAsync(
                collector.FileSources,
                tempProject,
                tempProjectDir,
                cancellationToken);
            _logger.LogInformation("Relocated {Count} external files", fileCount);

            // Step 6: Copy fonts
            progress?.Report((Strings.ExportingProject, 0.6));
            int fontCount = await _relocationService.RelocateFontsAsync(
                collector.FontFamilies,
                tempProjectDir,
                cancellationToken);
            _logger.LogInformation("Relocated {Count} font files", fontCount);

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

            return true;
        }
        catch (OperationCanceledException)
        {
            LogExportCancelled();
            throw;
        }
        catch (Exception ex)
        {
            return LogExportError(ex);
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

    [ExcludeFromCodeCoverage]
    private bool LogExportError(Exception ex)
    {
        _logger.LogError(ex, "Failed to export project");
        return false;
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
