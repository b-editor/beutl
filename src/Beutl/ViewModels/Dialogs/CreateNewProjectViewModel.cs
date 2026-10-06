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

    internal CreateNewProjectViewModel(
        ProjectService projectService,
        IProjectVersionControlInitializer? versionControlInitializer,
        Func<CancellationToken, Task<GitIdentity?>>? requestIdentityAsync)
    {
        _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
        _versionControlInitializer = versionControlInitializer;
        _requestIdentityAsync = requestIdentityAsync;
        Location.Value = GetDefaultLocation();
        Name.Value = NewDocumentValidation.UniqueName(Location.Value, "Project");
        _ = DetectGitAsync();

        Name.SetValidateNotifyError(n => NewDocumentValidation.ValidateName(n, Location.Value));
        Location.Subscribe(_ => Name.ForceValidate());
        Size.SetValidateNotifyError(NewDocumentValidation.ValidateSize);
        FrameRate.SetValidateNotifyError(NewDocumentValidation.ValidatePositive);
        SampleRate.SetValidateNotifyError(NewDocumentValidation.ValidatePositive);

        CanCreate = Name.CombineLatest(Location, Size, FrameRate, SampleRate)
            .Select(t =>
            {
                (string name, string location, PixelSize size, int framerate, int samplerate) = t;
                return NewDocumentValidation.CanCreateAt(name, location, size)
                    && framerate > 0
                    && samplerate > 0;
            })
            .ToReadOnlyReactivePropertySlim();
        Create = new AsyncReactiveCommand(CanCreate);
        Create.Subscribe(async () =>
        {
            // Capture only an option that was visible before creation started. Git detection can
            // finish while the project is being written, but that must not silently opt the user in.
            bool initializeVersionControl = IsGitAvailable.Value && TrackHistory.Value;

            // The first version is recorded before the project opens, so the editor never appears while
            // it still has to wait for Git.
            INewProjectVersionControlSetup? versionControl =
                initializeVersionControl
                && _versionControlInitializer is not null
                && _requestIdentityAsync is not null
                    ? _versionControlInitializer.BeginNewProject(_requestIdentityAsync)
                    : null;
            try
            {
                // CreateProject surfaces failures to the user itself, so no fallback notification here.
                await _projectService.CreateProject(
                    Size.Value.Width, Size.Value.Height,
                    FrameRate.Value, SampleRate.Value,
                    Name.Value,
                    Location.Value,
                    versionControl is null
                        ? null
                        : (project, cancellationToken) => InitializeVersionControlAsync(
                            versionControl,
                            project,
                            cancellationToken));
            }
            finally
            {
                if (versionControl is not null)
                {
                    await versionControl.DisposeAsync();
                }
            }
        });
    }

    // A failed or canceled initialization still opens the project, untracked, so the user keeps what they
    // created.
    private static async Task InitializeVersionControlAsync(
        INewProjectVersionControlSetup versionControl,
        Project project,
        CancellationToken cancellationToken)
    {
        try
        {
            await versionControl.InitializeAsync(project, cancellationToken);
        }
        catch (Exception ex)
        {
            await ex.Handle();
        }
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
            TrackHistory.Value = false;
            IsGitAvailable.Value = false;
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
}
