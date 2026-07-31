using Avalonia;

using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using Beutl.Logging;
using Beutl.Services;

using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public sealed class CreateNewProjectViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<CreateNewProjectViewModel>();
    private readonly ProjectService _projectService;
    private readonly IProjectVersionControlInitializer? _versionControlInitializer;
    private readonly Func<CancellationToken, Task<GitIdentity?>>? _requestIdentityAsync;

    public CreateNewProjectViewModel(ProjectService projectService)
        : this(projectService, versionControlInitializer: null, requestIdentityAsync: null)
    {
    }

    public CreateNewProjectViewModel(
        ProjectService projectService,
        IProjectVersionControlInitializer? versionControlInitializer,
        Func<CancellationToken, Task<GitIdentity?>>? requestIdentityAsync)
    {
        _projectService = projectService;
        _versionControlInitializer = versionControlInitializer;
        _requestIdentityAsync = requestIdentityAsync;
        Location.Value = GetDefaultLocation();
        Name.Value = GenProjectName(Location.Value);
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
            // Capture only an option that was visible before creation started. Git detection can
            // finish while the project is being written, but that must not silently opt the user in.
            bool initializeVersionControl = IsGitAvailable.Value && TrackHistory.Value;

            // CreateProject surfaces failures to the user itself, so no fallback notification here.
            Project? project = await _projectService.CreateProject(
                Size.Value.Width, Size.Value.Height,
                FrameRate.Value, SampleRate.Value,
                Name.Value,
                Location.Value);
            if (project is not null
                && initializeVersionControl
                && _versionControlInitializer is not null
                && _requestIdentityAsync is not null)
            {
                await _versionControlInitializer.InitializeCurrentProjectAsync(
                    _requestIdentityAsync,
                    CancellationToken.None);
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
        if (_versionControlInitializer is null)
        {
            return;
        }

        try
        {
            GitAvailability availability = await _versionControlInitializer.GetAvailabilityAsync(
                CancellationToken.None);
            bool isAvailable = availability.State == GitAvailabilityState.Installed;
            TrackHistory.Value = isAvailable
                                 && GlobalConfiguration.Instance.VersionControlConfig.EnableForNewProjects;
            IsGitAvailable.Value = isAvailable;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            TrackHistory.Value = false;
            IsGitAvailable.Value = false;
            _logger.LogWarning(ex, "Failed to detect Git while creating a project.");
        }
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
