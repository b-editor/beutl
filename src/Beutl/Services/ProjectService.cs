using System.Reactive.Subjects;
using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
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
                    Title = Message.ProjectVersionMismatch_Title,
                    Content = string.Format(Message.ProjectVersionMismatch_Content, minVersion),
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
            NotificationService.ShowInformation("", Message.CouldNotOpenProject);
        }
    }

    public void CloseProject()
    {
        if (_app.Project is { } project)
        {
            // 値を発行
            _projectObservable.OnNext((New: null, project));
            _app.Project = null;
            _logger.LogInformation("Closed project. Project: {Project}", project.Uri);
        }
    }

    public Project? CreateProject(int width, int height, int framerate, int samplerate, string name, string location)
    {
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
                Uri = UriHelper.CreateFromPath(Path.Combine(location, name, $"{name}.{Constants.SceneFileExtension}")),
            };
            var project = new Project()
            {
                Items = { scene },
                Uri  = UriHelper.CreateFromPath(Path.Combine(location, $"{name}.{Constants.ProjectFileExtension}")),
                Variables =
                {
                    [ProjectVariableKeys.FrameRate] = framerate.ToString(),
                    [ProjectVariableKeys.SampleRate] = samplerate.ToString(),
                }
            };

            CoreSerializer.StoreToUri(scene, scene.Uri);
            CoreSerializer.StoreToUri(project, project.Uri);

            // 値を発行
            _projectObservable.OnNext((New: project, null));
            _app.Project = project;

            AddToRecentProjects(Uri.UnescapeDataString(project.Uri.LocalPath));
            _logger.LogInformation("Created new project. Name: {Name}, Location: {Location}, Width: {Width}, Height: {Height}, Framerate: {Framerate}, Samplerate: {Samplerate}", name, location, width, height, framerate, samplerate);

            return project;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogError(ex, "Unable to create the project. Name: {Name}, Location: {Location}", name, location);
            return null;
        }
    }

    private static void AddToRecentProjects(string file)
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        viewConfig.UpdateRecentProject(file);
        viewConfig.UpdateRecentFile(file);
    }
}
