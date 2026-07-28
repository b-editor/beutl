using Avalonia;

using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using Beutl.Services;

using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public sealed class CreateNewProjectViewModel
{
    private readonly ProjectService _projectService;
    private readonly VersionControlCoordinator? _versionControlCoordinator;
    private readonly Func<IProjectVersionControlService, Task<bool>>? _requestIdentityAsync;

    public CreateNewProjectViewModel(ProjectService projectService)
        : this(projectService, versionControlCoordinator: null, requestIdentityAsync: null)
    {
    }

    public CreateNewProjectViewModel(
        ProjectService projectService,
        VersionControlCoordinator? versionControlCoordinator,
        Func<IProjectVersionControlService, Task<bool>>? requestIdentityAsync)
    {
        _projectService = projectService;
        _versionControlCoordinator = versionControlCoordinator;
        _requestIdentityAsync = requestIdentityAsync;
        Location.Value = GetDefaultLocation();
        Name.Value = GenProjectName(Location.Value);
        TrackHistory.Value = GlobalConfiguration.Instance.VersionControlConfig.EnableForNewProjects;
        _ = DetectGitAsync();

        Name.SetValidateNotifyError(n =>
        {
            if (n == string.Empty || n == null || n.IndexOfAny(Path.GetInvalidFileNameChars()) > -1)
            {
                return MessageStrings.InvalidString;
            }
            else if (Directory.Exists(Path.Combine(Location.Value, n)))
            {
                return MessageStrings.AlreadyExists;
            }
            else
            {
                return null;
            }
        });
        Location.Subscribe(_ => Name.ForceValidate());
        Size.SetValidateNotifyError(s =>
        {
            if (s.Width <= 0 || s.Height <= 0)
            {
                return MessageStrings.ValueLessThanOrEqualToZero;
            }
            else
            {
                return null;
            }
        });
        FrameRate.SetValidateNotifyError(n =>
        {
            if (n <= 0)
            {
                return MessageStrings.ValueLessThanOrEqualToZero;
            }
            else
            {
                return null;
            }
        });
        SampleRate.SetValidateNotifyError(n =>
        {
            if (n <= 0)
            {
                return MessageStrings.ValueLessThanOrEqualToZero;
            }
            else
            {
                return null;
            }
        });

        CanCreate = Name.CombineLatest(Location, Size, FrameRate, SampleRate)
            .Select(t =>
            {
                (string name, string location, PixelSize size, int framerate, int samplerate) = t;

                if (location != null && name != null)
                {
                    return !Directory.Exists(Path.Combine(location, name)) &&
                        size.Width > 0 &&
                        size.Height > 0 &&
                        framerate > 0 &&
                        samplerate > 0;
                }
                else return false;
            })
            .ToReadOnlyReactivePropertySlim();
        Create = new AsyncReactiveCommand(CanCreate);
        Create.Subscribe(async () =>
        {
            // CreateProject surfaces failures to the user itself, so no fallback notification here.
            Project? project = await _projectService.CreateProject(
                Size.Value.Width, Size.Value.Height,
                FrameRate.Value, SampleRate.Value,
                Name.Value,
                Location.Value);
            if (project is not null
                && TrackHistory.Value
                && IsGitAvailable.Value
                && _versionControlCoordinator is not null
                && _requestIdentityAsync is not null)
            {
                await _versionControlCoordinator.InitializeCurrentProjectAsync(_requestIdentityAsync);
            }
        });
    }

    public ReactiveProperty<PixelSize> Size { get; } = new(new PixelSize(1920, 1080));

    public ReactiveProperty<int> FrameRate { get; } = new(30);

    public ReactiveProperty<int> SampleRate { get; } = new(44100);

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<string> Location { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanCreate { get; }

    public AsyncReactiveCommand Create { get; }

    public ReactivePropertySlim<bool> TrackHistory { get; } = new();

    public ReactivePropertySlim<bool> IsGitAvailable { get; } = new();

    private async Task DetectGitAsync()
    {
        if (_versionControlCoordinator is null)
        {
            return;
        }

        GitAvailability availability = await _versionControlCoordinator.GetAvailabilityAsync();
        IsGitAvailable.Value = availability.State == GitAvailabilityState.Installed;
    }

    private static string GetDefaultLocation()
    {
        ViewConfig config = GlobalConfiguration.Instance.ViewConfig;
        try
        {
            if (config.RecentProjects.FirstOrDefault() is { } last)
            {
                ReadOnlySpan<char> span = last.AsSpan();
                return new string(Path.GetDirectoryName(Path.GetDirectoryName(span)));
            }
        }
        catch
        {
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private static string GenProjectName(string location)
    {
        const string name = "Project";
        int n = 1;

        while (Directory.Exists(Path.Combine(location, name + n)))
        {
            n++;
        }

        return name + n;
    }
}
