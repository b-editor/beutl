using System.Reactive.Subjects;
using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services.Tutorials;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Reactive.Bindings;

namespace Beutl.Services;

public sealed class ProjectService
{
    private readonly Subject<(Project? New, Project? Old)> _projectObservable = new();
    private readonly ReadOnlyReactivePropertySlim<bool> _isOpened;
    private readonly BeutlApplication _app = BeutlApplication.Current;
    private readonly ILogger _logger = Log.CreateLogger<ProjectService>();

    public ProjectService()
    {
        CurrentProject = _app.GetObservable(BeutlApplication.ProjectProperty)
            .ToReadOnlyReactivePropertySlim();
        _isOpened = CurrentProject.Select(v => v != null).ToReadOnlyReactivePropertySlim();
    }

    public static ProjectService Current { get; } = new();

    public IObservable<(Project? New, Project? Old)> ProjectObservable => _projectObservable;

    public IReadOnlyReactiveProperty<Project?> CurrentProject { get; }

    public IReadOnlyReactiveProperty<bool> IsOpened => _isOpened;

    private static async Task<(NuGetVersion AppVersion, NuGetVersion MinVersion)> GetProjectVersion(string file)
    {
        await using var stream = File.OpenRead(file);
        var node = await JsonNode.ParseAsync(stream);
        string? appVersion = (string?)node?["appVersion"];
        string? minAppVersion = (string?)node?["minAppVersion"];
        if (appVersion == null || minAppVersion == null)
        {
            throw new InvalidOperationException("The project file does not contain version information.");
        }

        return (NuGetVersion.Parse(appVersion), NuGetVersion.Parse(minAppVersion));
    }

    public async Task OpenProject(string file)
    {
        await App.WaitLoadingExtensions();

        using Activity? activity = Telemetry.StartActivity();
        try
        {
            CloseProject();

            (NuGetVersion appVersion, NuGetVersion minVersion) = await GetProjectVersion(file);
            activity?.SetTag(nameof(appVersion), appVersion.ToString());
            activity?.SetTag(nameof(minVersion), minVersion.ToString());
            if (minVersion > NuGetVersion.Parse(BeutlApplication.Version) &&
                !Preferences.Default.Get("ProjectService.SkipVersionCheck", false))
            {
                var dialog = new ContentDialog
                {
                    Title = MessageStrings.ProjectVersionMismatch_Title,
                    Content = string.Format(MessageStrings.ProjectVersionMismatch_Content, minVersion),
                    PrimaryButtonText = Strings.Close
                };
                await dialog.ShowAsync();
                return;
            }

            var project = CoreSerializer.RestoreFromUri<Project>(UriHelper.CreateFromPath(file));

            _app.Project = project;
            // 値を発行
            _projectObservable.OnNext((New: project, null));

            AddToRecentProjects(file);
            _logger.LogInformation("Opened project. File: {File}, AppVersion: {AppVersion}, MinVersion: {MinVersion}", file, appVersion, minVersion);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogError(ex, "Unable to open the project. File: {File}", file);
            NotificationService.ShowInformation(Strings.Project, MessageStrings.FailedToOpenProject);
        }
    }

    public void CloseProject()
    {
        if (_app.Project is { } project)
        {
            // 値を発行
            _projectObservable.OnNext((New: null, project));
            _app.Project = null;
            GlobalConfiguration.Instance.ViewConfig.LastOpenedProjectFile = null;
            _logger.LogInformation("Closed project. Project: {Project}", project.Uri);
        }
    }

    public async Task<Project?> CreateProject(int width, int height, int framerate, int samplerate, string name, string location)
    {
        await App.WaitLoadingExtensions();

        using Activity? activity = Telemetry.StartActivity();
        activity?.SetTag(nameof(width), width);
        activity?.SetTag(nameof(height), height);
        activity?.SetTag(nameof(framerate), framerate);
        activity?.SetTag(nameof(samplerate), samplerate);
        try
        {
            CloseProject();

            location = Path.Combine(location, name);
            var scene = new Scene(width, height, name)
            {
                Uri = UriHelper.CreateFromPath(Path.Combine(location, name, $"{name}.{EditorConstants.SceneFileExtension}")),
            };
            var project = new Project()
            {
                Items = { scene },
                Uri = UriHelper.CreateFromPath(Path.Combine(location, $"{name}.{EditorConstants.ProjectFileExtension}")),
                Variables =
                {
                    [ProjectVariableKeys.FrameRate] = framerate.ToString(),
                    [ProjectVariableKeys.SampleRate] = samplerate.ToString(),
                }
            };

            CoreSerializer.StoreToUri(scene, scene.Uri);
            ProjectPersistence.PersistOrRollback(
                () => CoreSerializer.StoreToUri(project, project.Uri),
                () =>
                {
                    // The project file could not be written, so the scene file just persisted is
                    // orphaned on disk. Remove it (best-effort) to keep disk consistent.
                    try
                    {
                        File.Delete(scene.Uri.LocalPath);
                    }
                    catch (Exception deleteEx)
                    {
                        _logger.LogWarning(deleteEx, "Failed to delete orphaned scene file: {Uri}", scene.Uri);
                    }
                });

            // 値を発行
            _projectObservable.OnNext((New: project, null));
            _app.Project = project;

            AddToRecentProjects(project.Uri.LocalPath);
            _logger.LogInformation("Created new project. Name: {Name}, Location: {Location}, Width: {Width}, Height: {Height}, Framerate: {Framerate}, Samplerate: {Samplerate}", name, location, width, height, framerate, samplerate);

            return project;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogError(ex, "Unable to create the project. Name: {Name}, Location: {Location}", name, location);
            // Surface the actual failure (disk full, permission denied, ...) to the user instead of a
            // generic "operation failed". Mirrors the scene-create path, which shows the message too.
            NotificationService.ShowError(Strings.Error, ex.Message);
            return null;
        }
    }

    private static void AddToRecentProjects(string file)
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        viewConfig.UpdateRecentProject(file);
        viewConfig.UpdateRecentFile(file);
        viewConfig.LastOpenedProjectFile = file;
    }
}
