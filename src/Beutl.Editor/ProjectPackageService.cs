using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.ExceptionServices;
using Beutl.IO;
using Beutl.Language;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Utilities;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor;

/// <summary>
/// Result of a project export operation.
/// </summary>
/// <param name="Success">Whether the ZIP was written. Cancellation does not set this to <c>false</c> — it propagates as <see cref="OperationCanceledException"/>.</param>
/// <param name="FailedResources">Resources that could not be relocated. Non-empty with <see cref="Success"/> means a partial export.</param>
public sealed record ExportResult(bool Success, IReadOnlyList<string> FailedResources);

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
            progress?.Report((Strings.ExportingProject, 0.0));
            // The copy can contain private files even when the final package is shared.
            tempDir = Directory.CreateTempSubdirectory("beutl_export_").FullName;

            string projectDir = Path.GetDirectoryName(project.Uri.LocalPath)!;
            string tempProjectDir = Path.Combine(tempDir, Path.GetFileName(projectDir));
            progress?.Report((Strings.ExportingProject, 0.1));
            string excludedOutputPath = FilePathComparison.ResolveCanonicalPath(outputPath);
            await CopyDirectoryAsync(projectDir, tempProjectDir, excludedOutputPath, cancellationToken);

            // Read with the original base URI. Eager file sources (models in
            // particular) must resolve their external references before rebasing the copy.
            string tempProjectFile = Path.Combine(tempProjectDir, Path.GetFileName(project.Uri.LocalPath));
            Uri tempProjectUri = UriHelper.CreateFromPath(tempProjectFile);
            progress?.Report((Strings.ExportingProject, 0.2));
            Project tempProject = CoreSerializer.RestoreFromUri<Project>(project.Uri);
            ResourceRelocationService.RebaseProjectDirectory(tempProject, projectDir, tempProjectDir);

            progress?.Report((Strings.ExportingProject, 0.3));
            VirtualProjectRoot virtualRoot = new();
            virtualRoot.AttachProject(tempProject);

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

            progress?.Report((Strings.ExportingProject, 0.8));
            // Rebase and relocation are complete. Persist hashes for the packaged
            // URIs, including internal assets, instead of the original machine's paths.
            var fingerprintService = new MissingMediaService();
            foreach (Scene scene in ExternalResourceCollector.EnumerateObjects(tempProject).OfType<Scene>())
                await fingerprintService.UpdateFingerprintsAsync(scene, cancellationToken);
            foreach (CoreObject obj in ExternalResourceCollector.EnumerateObjects(tempProject))
            {
                if (obj.Uri is { IsFile: true } uri
                    && !FilePathComparison.IsSameOrDescendant(tempProjectDir, uri.LocalPath))
                    throw new IOException($"Cannot save an export sidecar outside staging: {uri}");
            }
            CoreSerializer.StoreToUri(tempProject, tempProjectUri);

            virtualRoot.DetachProject();

            progress?.Report((Strings.ExportingProject, 0.9));
            using (var output = new StagedOutputFile(outputPath))
            {
                await Task.Run(() => ZipFile.CreateFromDirectory(tempProjectDir, output.TemporaryPath), cancellationToken);
                output.Commit(cancellationToken);
            }

            progress?.Report((Strings.ExportingProject, 1.0));
            _logger.LogInformation("Project exported successfully to {OutputPath}", outputPath);

            List<string> failedResources = [.. fileResult.FailedResources, .. fontResult.FailedResources];
            return new ExportResult(true, failedResources);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Export operation was cancelled");
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
    /// <returns>The imported project, or <c>null</c> if import failed.</returns>
    /// <exception cref="UnsupportedFileVersionException">
    /// The packaged project, or a file it references, needs a newer version of Beutl. Nothing is imported.
    /// </exception>
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

        string? stagingDirectory = null;
        string? publishedDirectory = null;
        try
        {
            progress?.Report((Strings.ImportingProject, 0.0));
            cancellationToken.ThrowIfCancellationRequested();
            destinationDirectory = Path.GetFullPath(destinationDirectory);
            Directory.CreateDirectory(destinationDirectory);
            stagingDirectory = Path.Combine(destinationDirectory, $".beutl-import-{Guid.NewGuid():N}");
            if (OperatingSystem.IsWindows())
                Directory.CreateDirectory(stagingDirectory);
            else
                Directory.CreateDirectory(stagingDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            // Keep untrusted contents private until extraction, sanitization and validation finish.
            string projectDir = Path.Combine(stagingDirectory, "project");
            progress?.Report((Strings.ImportingProject, 0.3));
            await ZipFile.ExtractToDirectoryAsync(packagePath, projectDir, cancellationToken);
            await Task.Run(() => RemoveGitMetadata(projectDir, cancellationToken), cancellationToken);

            progress?.Report((Strings.ImportingProject, 0.6));
            string? projectFile = Directory.GetFiles(projectDir, $"*.{EditorConstants.ProjectFileExtension}", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();

            if (projectFile == null)
            {
                _logger.LogError("No project file found in package");
                return null;
            }

            progress?.Report((Strings.ImportingProject, 0.8));
            _ = CoreSerializer.RestoreFromUri<Project>(UriHelper.CreateFromPath(projectFile));
            publishedDirectory = PublishImportedDirectory(
                projectDir, destinationDirectory, Path.GetFileNameWithoutExtension(packagePath), cancellationToken);

            // Re-read at its final location so relative scene/resource URIs never point at staging.
            Uri projectUri = UriHelper.CreateFromPath(Path.Combine(publishedDirectory, Path.GetFileName(projectFile)));
            Project project = CoreSerializer.RestoreFromUri<Project>(projectUri);
            progress?.Report((Strings.ImportingProject, 1.0));
            _logger.LogInformation("Project imported successfully from {PackagePath} to {ProjectDir}",
                packagePath, publishedDirectory);
            publishedDirectory = null;
            return project;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Import operation was cancelled");
            throw;
        }
        catch (Exception ex) when (ExceptionHelpers.Find<UnsupportedFileVersionException>(ex) is { } unsupported)
        {
            // Reported to the caller, which can name the version the project needs. A file read in
            // parallel arrives wrapped.
            _logger.LogWarning(ex, "The packaged project needs a newer version of Beutl");
            ExceptionDispatchInfo.Capture(unsupported).Throw();
            throw; // Not reached: the line above throws.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import project");
            return null;
        }
        finally
        {
            // Only remove directories owned by this import, never another import's destination.
            CleanupTempDirectory(publishedDirectory);
            CleanupTempDirectory(stagingDirectory);
        }
    }

    private static string PublishImportedDirectory(
        string sourceDirectory, string parentDirectory, string directoryName, CancellationToken cancellationToken)
    {
        for (int counter = 0; ; counter++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.Combine(parentDirectory, counter == 0 ? directoryName : $"{directoryName}_{counter}");
            try
            {
                // The validated directory is nonempty. A competing publication cannot replace it.
                Directory.Move(sourceDirectory, path);
                return path;
            }
            catch (IOException) when (Directory.Exists(path) || File.Exists(path))
            {
                // Another process may have claimed the name after we started extracting.
            }
        }
    }

    private static void RemoveGitMetadata(string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Resolve through the filesystem too, including aliases accepted by its name comparison.
        string gitPath = Path.Combine(directory, ".git");
        if (Directory.Exists(gitPath))
            Directory.Delete(gitPath, recursive: true);
        else if (File.Exists(gitPath))
            File.Delete(gitPath);

        foreach (string path in Directory.GetFileSystemEntries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetFileName(path), ".git", StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                else
                    File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                RemoveGitMetadata(path, cancellationToken);
            }
        }
    }

    private static async Task CopyDirectoryAsync(
        string sourceDir, string destDir, string excludedOutputPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Linked worktrees and submodules use a .git file rather than a directory.
            if (string.Equals(Path.GetFileName(file), ".git", StringComparison.OrdinalIgnoreCase))
                continue;
            // Replacing a package inside the project must not embed its previous contents,
            // including assets the user has since removed. Resolve aliases of the output too.
            if (string.Equals(FilePathComparison.ResolveCanonicalPath(file), excludedOutputPath, StringComparison.Ordinal))
                continue;
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            await ResourceRelocationService.CopyFileAsync(file, destFile, cancellationToken);
        }

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dirName = Path.GetFileName(subDir);

            // Share the current project, not local view state or recoverable repository history.
            if (dirName == EditorConstants.BeutlFolder || string.Equals(dirName, ".git", StringComparison.OrdinalIgnoreCase))
                continue;

            string destSubDir = Path.Combine(destDir, dirName);
            await CopyDirectoryAsync(subDir, destSubDir, excludedOutputPath, cancellationToken);
        }
    }
}
