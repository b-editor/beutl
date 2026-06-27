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

public static class ProjectOperations
{
    public static Project CreateProject(ProjectCreateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string projectPath = Path.GetFullPath(options.ProjectPath);
        string projectName = options.Name ?? Path.GetFileNameWithoutExtension(projectPath);
        string projectDirectory = Path.GetDirectoryName(projectPath)
                                  ?? throw new InvalidOperationException("Project path must have a directory.");
        string sceneDirectory = Path.Combine(projectDirectory, projectName);
        string scenePath = Path.Combine(sceneDirectory, $"{projectName}.{EditorConstants.SceneFileExtension}");

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
        string sceneDirectory = Path.Combine(projectDirectory, sceneName);
        string scenePath = Path.Combine(sceneDirectory, $"{sceneName}.{EditorConstants.SceneFileExtension}");

        var scene = new Scene(options.Width, options.Height, sceneName)
        {
            Uri = CreateFileUri(scenePath),
            Start = options.Start,
            Duration = options.Duration
        };

        project.Items.Add(scene);
        return scene;
    }

    public static void Save(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.Uri is null)
        {
            throw new InvalidOperationException("Project must have a Uri before it can be saved.");
        }

        foreach (Scene scene in project.Items.OfType<Scene>())
        {
            EnsureSceneUri(project, scene);
            EnsureElementUris(scene);
        }

        CoreSerializer.StoreToUri(project, project.Uri);
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
        string sceneDirectory = Path.Combine(projectDirectory, sceneName);
        scene.Uri = CreateFileUri(Path.Combine(sceneDirectory, $"{sceneName}.{EditorConstants.SceneFileExtension}"));
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
