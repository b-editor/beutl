using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Editor;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Sessions;

public sealed record ProjectCreateOptions(
    string ProjectPath,
    int Width,
    int Height,
    int FrameRate,
    TimeSpan Duration,
    int SampleRate = 48000,
    string? Name = null);

public sealed record SceneCreateOptions(
    int Width,
    int Height,
    TimeSpan Start,
    TimeSpan Duration,
    string? Name = null);

public sealed record ProjectUriState(
    Uri? ProjectUri,
    IReadOnlyList<(Scene Scene, Uri? Uri)> Scenes,
    IReadOnlyList<(Element Element, Uri? Uri)> Elements);

public static class ProjectOperations
{
    public static Project CreateProject(ProjectCreateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string projectPath = Path.GetFullPath(options.ProjectPath);
        string projectName = options.Name ?? Path.GetFileNameWithoutExtension(projectPath);
        string projectDirectory = Path.GetDirectoryName(projectPath)
                                  ?? throw new InvalidOperationException("Project path must have a directory.");
        string scenePath = ReserveUniqueScenePath(
            projectDirectory,
            projectName,
            new HashSet<string>(StringComparer.FromComparison(PathComparison.ForCurrentPlatform)));

        var scene = new Scene(options.Width, options.Height, projectName)
        {
            Uri = CreateFileUri(scenePath),
            Duration = options.Duration
        };

        var project = new Project
        {
            Uri = CreateFileUri(projectPath),
            Items = { scene },
            Variables =
            {
                [ProjectVariableKeys.FrameRate] = options.FrameRate.ToString(),
                [ProjectVariableKeys.SampleRate] = options.SampleRate.ToString(),
            }
        };

        return project;
    }

    public static Scene AddScene(Project project, SceneCreateOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);

        if (project.Uri is null)
        {
            throw new InvalidOperationException("Project must have a Uri before scenes can be added.");
        }

        string projectDirectory = Path.GetDirectoryName(project.Uri.LocalPath)
                                  ?? throw new InvalidOperationException("Project Uri must have a directory.");
        string sceneName = options.Name ?? $"Scene{project.Items.Count + 1}";
        ValidateSceneName(sceneName);

        var scene = new Scene(options.Width, options.Height, sceneName)
        {
            Uri = DeriveUniqueSceneUri(project, projectDirectory, sceneName, exclude: null),
            Start = options.Start,
            Duration = options.Duration
        };

        project.Items.Add(scene);
        return scene;
    }

    // Save mutates the open graph BEFORE the fallible project write (rehoming out-of-project
    // sidecars, nulling duplicates, assigning missing URIs); a failed save must be able to put the
    // original locations back so the project is left exactly as it was.
    public static ProjectUriState CaptureUriState(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var scenes = new List<(Scene Scene, Uri? Uri)>();
        var elements = new List<(Element Element, Uri? Uri)>();
        foreach (Scene scene in project.Items.OfType<Scene>())
        {
            scenes.Add((scene, scene.Uri));
            foreach (Element element in scene.Children)
            {
                elements.Add((element, element.Uri));
            }
        }

        return new ProjectUriState(project.Uri, scenes, elements);
    }

    public static void RestoreUriState(Project project, ProjectUriState state)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(state);

        project.Uri = state.ProjectUri;
        foreach ((Scene scene, Uri? uri) in state.Scenes)
        {
            scene.Uri = uri;
        }

        foreach ((Element element, Uri? uri) in state.Elements)
        {
            element.Uri = uri;
        }
    }

    public static void Save(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.Uri is null)
        {
            throw new InvalidOperationException("Project must have a Uri before it can be saved.");
        }

        string projectDirectory = Path.GetDirectoryName(project.Uri.LocalPath)
                                  ?? throw new InvalidOperationException("Project Uri must have a directory.");

        foreach (Scene scene in project.Items.OfType<Scene>())
        {
            RehomeSidecarsOutsideProject(project, projectDirectory, scene);
        }

        // Two scenes or two elements loaded from a hand-edited project can carry the same in-project
        // sidecar Uri; StoreToUri writes each referenced sidecar to its Uri, so the later object would
        // overwrite the earlier one and a reopen would lose it. Keep the first occurrence and null the
        // rest so the Ensure* helpers below regenerate them on distinct paths.
        NullDuplicateSidecarUris(project);

        foreach (Scene scene in project.Items.OfType<Scene>())
        {
            EnsureSceneUri(project, scene);
            EnsureElementUris(scene);
        }

        CoreSerializer.StoreToUri(project, project.Uri);
    }

    private static void NullDuplicateSidecarUris(Project project)
    {
        var used = new HashSet<string>(StringComparer.FromComparison(PathComparison.ForCurrentPlatform));
        foreach (Scene scene in project.Items.OfType<Scene>())
        {
            if (scene.Uri is not null && !used.Add(ResolveSidecarPath(scene.Uri)))
            {
                scene.Uri = null;
            }

            foreach (Element element in scene.Children)
            {
                if (element.Uri is not null && !used.Add(ResolveSidecarPath(element.Uri)))
                {
                    element.Uri = null;
                }
            }
        }
    }

    private static string ResolveSidecarPath(Uri uri)
    {
        return PathBoundary.ResolveDeepestExistingTarget(Path.GetFullPath(uri.LocalPath));
    }

    // A project loaded from disk can carry scene/element sidecar URIs pointing outside the project
    // directory (hand-edited or malicious). StoreToUri writes each referenced sidecar to its own Uri,
    // so drop any that escape the project tree and let the Ensure* helpers regenerate them inside it.
    private static void RehomeSidecarsOutsideProject(Project project, string projectDirectory, Scene scene)
    {
        if (scene.Uri is not null && !IsInsideDirectory(projectDirectory, scene.Uri.LocalPath))
        {
            scene.Uri = null;
        }

        foreach (Element element in scene.Children)
        {
            if (element.Uri is not null && !IsInsideDirectory(projectDirectory, element.Uri.LocalPath))
            {
                element.Uri = null;
            }
        }
    }

    private static bool IsInsideDirectory(string directory, string candidate)
    {
        // Resolve both sides through symlinks: a purely textual check would accept an in-project
        // sidecar Uri that names a symlink whose target is outside, letting StoreToUri write through
        // the link and preserve the boundary escape.
        string root = Path.TrimEndingDirectorySeparator(
            PathBoundary.ResolveDeepestExistingTarget(Path.GetFullPath(directory)));
        string full = PathBoundary.ResolveDeepestExistingTarget(Path.GetFullPath(candidate));
        StringComparison comparison = PathComparison.ForCurrentPlatform;
        return full.StartsWith(root + Path.DirectorySeparatorChar, comparison)
               || string.Equals(full, root, comparison);
    }

    internal static void EnsureElementUris(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (scene.Uri is null)
        {
            throw new InvalidOperationException("Scene must have a Uri before its elements can be saved.");
        }

        string sceneDirectory = Path.GetDirectoryName(scene.Uri.LocalPath)
                                ?? throw new InvalidOperationException("Scene Uri must have a directory.");
        Directory.CreateDirectory(sceneDirectory);

        foreach (Element element in scene.Children)
        {
            if (element.Uri is null)
            {
                element.Uri = CreateFileUri(GenerateUniquePath(sceneDirectory, EditorConstants.ElementFileExtension));
            }
        }
    }

    private static void EnsureSceneUri(Project project, Scene scene)
    {
        if (scene.Uri is not null)
        {
            return;
        }

        string projectDirectory = Path.GetDirectoryName(project.Uri!.LocalPath)
                                  ?? throw new InvalidOperationException("Project Uri must have a directory.");
        string sceneName = string.IsNullOrWhiteSpace(scene.Name) ? $"Scene{project.Items.IndexOf(scene) + 1}" : scene.Name;
        ValidateSceneName(sceneName);
        scene.Uri = DeriveUniqueSceneUri(project, projectDirectory, sceneName, exclude: scene);
    }

    // Two scenes with the same name would otherwise resolve to the same <name>/<name>.scene sidecar
    // and overwrite each other on save; disambiguate the directory when it is already taken.
    private static Uri DeriveUniqueSceneUri(Project project, string projectDirectory, string sceneName, Scene? exclude)
    {
        var used = project.Items.OfType<Scene>()
            .Where(item => !ReferenceEquals(item, exclude) && item.Uri is not null)
            .Select(item => Path.GetDirectoryName(item.Uri!.LocalPath))
            .Where(dir => dir is not null)
            .Select(dir => Path.GetFullPath(dir!))
            .ToHashSet(StringComparer.FromComparison(PathComparison.ForCurrentPlatform));

        return CreateFileUri(ReserveUniqueScenePath(projectDirectory, sceneName, used));
    }

    internal static string ReserveUniqueScenePath(string sidecarRootDirectory, string sceneName, ISet<string> usedDirectories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sidecarRootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneName);
        ArgumentNullException.ThrowIfNull(usedDirectories);

        string candidateDir = Path.GetFullPath(Path.Combine(sidecarRootDirectory, sceneName));
        string? scenePath = null;
        for (int suffix = 2; !TryReserveSceneDirectory(candidateDir, sceneName, usedDirectories, out scenePath); suffix++)
        {
            candidateDir = Path.GetFullPath(Path.Combine(sidecarRootDirectory, $"{sceneName}-{suffix}"));
        }

        return scenePath!;
    }

    private static bool TryReserveSceneDirectory(
        string candidateDir,
        string sceneName,
        ISet<string> usedDirectories,
        out string? scenePath)
    {
        scenePath = Path.Combine(candidateDir, $"{sceneName}.{EditorConstants.SceneFileExtension}");
        if (usedDirectories.Contains(candidateDir)
            || FileSystemEntryExists(candidateDir)
            || FileSystemEntryExists(scenePath))
        {
            scenePath = null;
            return false;
        }

        usedDirectories.Add(candidateDir);
        return true;
    }

    private static bool FileSystemEntryExists(string path)
    {
        return Path.Exists(path) || new FileInfo(path).LinkTarget is not null;
    }

    // The name becomes a directory/file segment under the project, so it must be a single path
    // component or the derived Uri could escape the project directory (and the workspace).
    internal static bool IsValidSceneName(string name)
        => !string.IsNullOrWhiteSpace(name)
           && name is not ("." or "..")
           && !Path.IsPathRooted(name)
           && !name.Contains(Path.DirectorySeparatorChar)
           && !name.Contains(Path.AltDirectorySeparatorChar)
           && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static void ValidateSceneName(string name)
    {
        if (!IsValidSceneName(name))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"Invalid scene name '{name}'. Scene names must be a single path segment without separators or traversal.",
                name));
        }
    }

    private static string GenerateUniquePath(string directory, string extension)
    {
        string path;
        do
        {
            path = Path.Combine(directory, $"{Guid.NewGuid():N}.{extension}");
        }
        while (File.Exists(path));

        return path;
    }

    private static Uri CreateFileUri(string path)
    {
        return new Uri(Path.GetFullPath(path));
    }
}
