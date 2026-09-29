using System.Diagnostics.CodeAnalysis;
using Beutl.Configuration;
using Beutl.Engine;
using Beutl.Graphics3D.Models;
using Beutl.IO;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Editor;

/// <summary>
/// Result of a resource relocation operation.
/// </summary>
/// <param name="SuccessCount">Number of property updates (file sources) or font files (fonts) that were successfully relocated. Granularity differs from <paramref name="FailedResources"/>.Count.</param>
/// <param name="FailedResources">Identifiers of resources that could not be relocated. The string format depends on the failure path: local file paths for missing sources, "<c>uri (guid.property)</c>" for per-property URI rewrites that threw, full URI strings for file-copy failures, and font family names for font failures.</param>
public sealed record RelocationResult(int SuccessCount, IReadOnlyList<string> FailedResources);

/// <summary>
/// Service for copying resource files and rewriting their URIs.
/// </summary>
public class ResourceRelocationService
{
    private readonly ILogger _logger = Log.CreateLogger<ResourceRelocationService>();
    private readonly Func<string, IEnumerable<string>>? _fontFileFinder;

    /// <summary>
    /// Default constructor.
    /// </summary>
    public ResourceRelocationService()
    {
    }

    /// <summary>
    /// Constructor for testing. Allows customizing the font file search logic.
    /// </summary>
    /// <param name="fontFileFinder">Font file search function.</param>
    internal ResourceRelocationService(Func<string, IEnumerable<string>> fontFileFinder)
    {
        _fontFileFinder = fontFileFinder;
    }

    /// <summary>
    /// Copies file sources to the project's resources directory and updates their URIs.
    /// </summary>
    /// <param name="sources">The list of file sources to copy.</param>
    /// <param name="stagingProject">The project to apply URI updates to.</param>
    /// <param name="projectDirectory">The path of the project directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="RelocationResult"/> with success count and failed source identifiers.</returns>
    public virtual async Task<RelocationResult> RelocateFileSourcesAsync(
        IEnumerable<(Guid Object, string PropertyName, Uri OriginalUri)> sources,
        Project stagingProject,
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        string resourcesDir = Path.Combine(projectDirectory, "resources");
        Directory.CreateDirectory(resourcesDir);

        var references = sources.ToArray();
        var sceneDirectories = CreateSceneDirectories(references, stagingProject, resourcesDir, cancellationToken);
        int count = 0;
        List<string> failedResources = [];
        foreach (var group in references.GroupBy(i => i.OriginalUri))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var originalUri = group.Key;
            string sourceFilePath = Path.GetFullPath(originalUri.LocalPath);
            if (!File.Exists(sourceFilePath))
            {
                _logger.LogWarning("Source file not found: {FilePath}", sourceFilePath);
                failedResources.Add(sourceFilePath);
                continue;
            }

            string destFilePath;
            try
            {
                ModelSource? model = group.Select(item => GetFileSource(stagingProject, item.Object, item.PropertyName))
                    .OfType<ModelSource>().FirstOrDefault();
                if (model != null)
                {
                    destFilePath = await CopyModelAsync(model, resourcesDir, cancellationToken);
                }
                else
                {
                    var sceneDirectory = sceneDirectories.FirstOrDefault(pair => sourceFilePath.StartsWith(pair.Key, StringComparison.Ordinal));
                    destFilePath = sceneDirectory.Key != null
                        ? Path.Combine(sceneDirectory.Value, sourceFilePath[sceneDirectory.Key.Length..])
                        : GetUniqueFilePath(resourcesDir, Path.GetFileName(sourceFilePath));
                    Directory.CreateDirectory(Path.GetDirectoryName(destFilePath)!);
                    await CopyFileAsync(sourceFilePath, destFilePath, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to copy file: {Uri}", originalUri);
                failedResources.Add(originalUri.ToString());
                continue;
            }

            foreach ((Guid id, string prop, _) in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    UpdateUri(stagingProject, id, prop, new Uri(destFilePath));
                    if (GetFileSource(stagingProject, id, prop) is ModelSource relocatedModel
                        && ExternalResourceCollector.RequiresRelocation(relocatedModel, projectDirectory))
                    {
                        // Some formats retain absolute buffer/material references. The
                        // copied source is then incomplete even when its meshes can render.
                        failedResources.Add($"{originalUri} ({id}.{prop})");
                    }
                    count++;
                    _logger.LogDebug("Relocated file: {OriginalPath} -> {NewPath}", sourceFilePath, destFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update URI for {Id}.{Property}: {Uri}", id, prop, originalUri);
                    failedResources.Add($"{originalUri} ({id}.{prop})");
                }
            }
        }

        return new RelocationResult(count, failedResources);
    }

    private static Dictionary<string, string> CreateSceneDirectories(
        IEnumerable<(Guid Object, string PropertyName, Uri OriginalUri)> sources,
        Project project,
        string resourcesDirectory,
        CancellationToken cancellationToken)
    {
        // Scene include/exclude patterns are relative to the scene directory. Keep
        // each external tree separate so its **/*.belm glob cannot load other scenes' clips.
        // Only collected references are copied; unrelated files stay outside the package.
        var directories = new Dictionary<string, string>(StringComparer.Ordinal);
        var scenes = sources
            .Where(source => source.PropertyName == nameof(CoreObject.Uri) && FindObject(project, source.Object) is Scene)
            .Select(source => (Path: source.OriginalUri.LocalPath, Directory: Path.GetDirectoryName(Path.GetFullPath(source.OriginalUri.LocalPath))!))
            .OrderBy(scene => scene.Directory.Length);
        foreach (var scene in scenes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string prefix = Path.EndsInDirectorySeparator(scene.Directory)
                ? scene.Directory
                : scene.Directory + Path.DirectorySeparatorChar;
            if (directories.Keys.Any(parent => prefix.StartsWith(parent, StringComparison.Ordinal)))
                continue;

            string destination = GetUniqueFilePath(resourcesDirectory, Path.GetFileNameWithoutExtension(scene.Path));
            Directory.CreateDirectory(destination);
            directories.Add(prefix, destination);
        }
        return directories;
    }

    internal static void RebaseProjectDirectory(Project project, string sourceDirectory, string destinationDirectory)
    {
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDirectory)) + Path.DirectorySeparatorChar;
        var references = ExternalResourceCollector.Collect(project, destinationDirectory).FileSources.ToArray();
        foreach ((Guid id, string property, Uri uri) in references)
        {
            string path = Path.GetFullPath(uri.LocalPath);
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                // An internal model can depend on external buffers or materials.
                // Relocate its complete bundle before asking Assimp to reopen it.
                if (GetFileSource(project, id, property) is ModelSource model
                    && ExternalResourceCollector.RequiresRelocation(model, sourceDirectory))
                    continue;
                UpdateUri(project, id, property, new Uri(Path.Combine(destinationDirectory, path[prefix.Length..])));
            }
        }
    }

    private static IFileSource? GetFileSource(Project project, Guid id, string propertyName)
    {
        if (FindObject(project, id) is not { } obj) return null;
        if (propertyName == nameof(INodeMember.Property) && obj is INodeMember { Property: { } adapter })
            return adapter.GetValue() as IFileSource;
        if (obj is EngineObject engineObject
            && engineObject.Properties.FirstOrDefault(p => p.Name == propertyName)?.CurrentValue is IFileSource source)
            return source;
        var property = PropertyRegistry.FindRegistered(obj, propertyName);
        return property == null ? null : obj.GetValue(property) as IFileSource;
    }

    private static CoreObject? FindObject(Project project, Guid id)
        => ExternalResourceCollector.EnumerateObjects(project).FirstOrDefault(obj => obj.Id == id);

    private static async Task<string> CopyModelAsync(ModelSource model, string resourcesDirectory, CancellationToken token)
    {
        string mainPath = Path.GetFullPath(model.Uri.LocalPath);
        string[] files = model.Dependencies.Append(mainPath).Distinct(StringComparer.Ordinal).ToArray();
        string commonDirectory = Path.GetDirectoryName(mainPath)!;
        foreach (string file in files)
        {
            while (!FilePathComparison.IsSameOrDescendantCanonicalPath(commonDirectory, file))
                commonDirectory = Path.GetDirectoryName(commonDirectory)
                    ?? throw new IOException("Model dependencies must be on the same filesystem root.");
        }

        string prefix = Path.EndsInDirectorySeparator(commonDirectory) ? commonDirectory : commonDirectory + Path.DirectorySeparatorChar;
        string directory = GetUniqueFilePath(resourcesDirectory, Path.GetFileNameWithoutExtension(mainPath));
        Directory.CreateDirectory(directory);
        foreach (string file in files)
        {
            token.ThrowIfCancellationRequested();
            string destination = Path.Combine(directory, file[prefix.Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await CopyFileAsync(file, destination, token);
        }
        return Path.Combine(directory, mainPath[prefix.Length..]);
    }

    private static void UpdateUri(Project stagingProject, Guid id, string propertyName, Uri newUri)
    {
        if (GetFileSource(stagingProject, id, propertyName) is { } fileSource)
        {
            // This is a relocation of the same source, not a user choosing another model.
            // Replacing the property would rebuild Model3D.Children and discard their edits.
            fileSource.ReadFrom(newUri);
            return;
        }
        var obj = FindObject(stagingProject, id);

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
    /// Copies font files to the project's resources/fonts directory.
    /// </summary>
    /// <param name="fontFamilies">The list of font families to copy.</param>
    /// <param name="projectDirectory">The path of the project directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="RelocationResult"/> with success count and failed font family names.</returns>
    public virtual async Task<RelocationResult> RelocateFontsAsync(
        IEnumerable<FontFamily> fontFamilies,
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        string fontsDir = Path.Combine(projectDirectory, "resources", "fonts");
        Directory.CreateDirectory(fontsDir);

        HashSet<string> copiedFiles = [];
        int count = 0;
        List<string> failedResources = [];

        foreach (FontFamily fontFamily in fontFamilies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                IEnumerable<string> fontFiles = _fontFileFinder != null
                    ? _fontFileFinder(fontFamily.Name)
                    : FindFontFiles(fontFamily.Name, fontsDir);
                bool foundAnyFile = false;
                foreach (string sourceFilePath in fontFiles)
                {
                    foundAnyFile = true;
                    if (!copiedFiles.Add(sourceFilePath))
                        continue;

                    // The project copy already includes bundled fonts. Preserve them when
                    // sharing an imported project again, without making another copy each time.
                    if (File.Exists(sourceFilePath) && FilePathComparison.IsSameOrDescendant(fontsDir, sourceFilePath))
                    {
                        count++;
                        continue;
                    }

                    string fileName = Path.GetFileName(sourceFilePath);
                    string destFilePath = GetUniqueFilePath(fontsDir, fileName);

                    await CopyFileAsync(sourceFilePath, destFilePath, cancellationToken);

                    count++;
                    _logger.LogDebug("Relocated font: {OriginalPath} -> {NewPath}", sourceFilePath, destFilePath);
                }

                if (!foundAnyFile)
                {
                    _logger.LogWarning("No font files found for family: {FontFamily}", fontFamily.Name);
                    failedResources.Add(fontFamily.Name);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to relocate font: {FontFamily}", fontFamily.Name);
                failedResources.Add(fontFamily.Name);
            }
        }

        return new RelocationResult(count, failedResources);
    }

    /// <summary>
    /// Searches for font files matching the specified font family name.
    /// </summary>
    /// <remarks>
    /// This method contains OS-dependent logic and external dependencies (SKTypeface, GlobalConfiguration),
    /// so it is bypassed during testing via <see cref="_fontFileFinder"/>.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    private static IEnumerable<string> FindFontFiles(string fontFamilyName, string projectFontsDirectory)
    {
        // A material package installs its fonts under the home directory, which is not one
        // of the OS font directories the user configures.
        IReadOnlyList<string> fontDirs =
        [
            projectFontsDirectory,
            .. GlobalConfiguration.Instance.FontConfig.FontDirectories,
            BeutlEnvironment.GetMaterialsDirectoryPath()
        ];
        List<string> foundFiles = [];

        foreach (string fontDir in fontDirs)
        {
            if (!Directory.Exists(fontDir))
                continue;

            // An unreadable subtree under a scanned root would otherwise throw and discard
            // the matches already collected from the other font directories.
            foreach (string file in FontManager.EnumerateFontCandidates(fontDir))
            {
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
                    // Skip if the font file fails to load
                }
            }
            // Re-sharing a bundle must keep its font version, not add identically
            // named host fonts that could win the next import's family registration.
            if (fontDir == projectFontsDirectory && foundFiles.Count > 0)
                return foundFiles;
        }

        // Also search system fonts (platform-specific paths)
        string[] systemFontDirs = GetSystemFontDirectories();
        foreach (string fontDir in systemFontDirs)
        {
            if (!Directory.Exists(fontDir))
                continue;

            try
            {
                foreach (string file in Directory.EnumerateFiles(fontDir, "*.*", SearchOption.AllDirectories))
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
                        // Skip if the font file fails to load
                    }
                }
            }
            catch
            {
                // Skip if directory access fails
            }
        }

        return foundFiles;
    }

    /// <summary>
    /// Gets the system font directories.
    /// </summary>
    /// <remarks>
    /// This method contains OS-dependent branching and paths for other operating systems cannot be tested,
    /// so it is excluded from code coverage measurement.
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
    /// Gets a unique file path that does not conflict with existing files.
    /// </summary>
    private static string GetUniqueFilePath(string directory, string fileName)
    {
        string destFilePath = Path.Combine(directory, fileName);
        if (!Path.Exists(destFilePath))
            return destFilePath;

        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        int counter = 1;

        while (Path.Exists(destFilePath))
        {
            destFilePath = Path.Combine(directory, $"{fileNameWithoutExt}_{counter}{ext}");
            counter++;
        }

        return destFilePath;
    }

    /// <summary>
    /// Copies a file asynchronously.
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
