using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

// The project's counterpart of a scene's view state: the scene on screen when the project closed, so that the project
// opens on that scene again. It is kept in the view-state folder of the project's .beutl folder, which version control
// and exported packages leave out, so writing it needs no project-file lease, even while a worktree mutation runs.
internal static class ProjectViewState
{
    private const string SelectedSceneKey = "selected-scene";
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(ProjectViewState));

    // Named after the project file, as a folder can hold several projects. The name keeps the project file's
    // extension, so a scene's view state in the same folder does not share it.
    internal static string GetPath(string projectFile)
        => Path.Combine(
            Path.GetDirectoryName(projectFile) ?? string.Empty,
            EditorConstants.BeutlFolder,
            EditorConstants.ViewStateFolder,
            Path.GetFileName(projectFile) + ".config");

    internal static void SaveSelectedScene(Project project, ProjectItem scene)
    {
        if (project.Uri is not { IsFile: true } projectUri || scene.Uri is not { IsFile: true } sceneUri)
            return;

        string projectFile = projectUri.LocalPath;
        try
        {
            // Relative to the project, so the project still finds the scene after its folder moves.
            string relativePath = Path.GetRelativePath(Path.GetDirectoryName(projectFile)!, sceneUri.LocalPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            string file = GetPath(projectFile);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            new JsonObject { [SelectedSceneKey] = relativePath }.JsonSave(file);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The project closes either way, and opens on its last scene next time.
            s_logger.LogWarning(ex, "Failed to save the selected scene of {Project}.", projectFile);
        }
    }

    // The scene to open the project on, or null when none was saved or the project no longer holds it.
    internal static ProjectItem? FindSelectedScene(Project project)
    {
        if (project.Uri is not { IsFile: true } projectUri)
            return null;

        string projectFile = projectUri.LocalPath;
        string file = GetPath(projectFile);
        try
        {
            if (!File.Exists(file))
                return null;

            JsonNode? json;
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                json = JsonNode.Parse(stream);
            }

            if (json is not JsonObject state
                || !state.TryGetPropertyValueAsJsonValue(SelectedSceneKey, out string? relativePath)
                || string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }

            FilePathComparison.ResolutionContext paths = FilePathComparison.CreateResolutionContext();
            string selected = paths.ResolveCanonicalPath(
                Path.GetFullPath(relativePath, Path.GetDirectoryName(projectFile)!));
            return project.Items.FirstOrDefault(item => item.Uri is { IsFile: true } uri
                && string.Equals(paths.ResolveCanonicalPath(uri.LocalPath), selected, StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A state that cannot be read must not keep the project from opening; it opens on its last scene.
            s_logger.LogWarning(ex, "Failed to read the selected scene of {Project}.", projectFile);
            return null;
        }
    }

    // A project deleted on its own leaves its folder behind, and a later project of the same name must not open on
    // its scene.
    internal static void Delete(string projectFile)
    {
        try
        {
            File.Delete(GetPath(projectFile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            s_logger.LogDebug(ex, "Failed to delete the view state of {Project}.", projectFile);
        }
    }
}
