using Avalonia.Collections;

using Beutl.Api.Objects;

using Reactive.Bindings;

using Serilog;

namespace Beutl.ViewModels.ExtensionsPages.DiscoverPages;

public sealed class UserProfilePageViewModel : BasePageViewModel
{
    private readonly ILogger _logger = Log.ForContext<UserProfilePageViewModel>();
    private readonly CompositeDisposable _disposables = new();

    public UserProfilePageViewModel(Profile profile)
    {
        Profile = profile;
        Refresh = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                try
                {
                    using (await Profile.Lock.LockAsync())
                    {
                        IsBusy.Value = true;
                        await Profile.RefreshAsync();
                        await RefreshPackages();
                    }
                }
                catch (Exception e)
                {
                    ErrorHandle(e);
                    _logger.Error(e, "An unexpected error has occurred.");
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        Refresh.Execute();

        More = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                try
                {
                    IsBusy.Value = true;
                    using (await Profile.Lock.LockAsync())
                    {
                        await MoreLoadPackages();
                    }
                }
                catch (Exception e)
                {
                    ErrorHandle(e);
                    _logger.Error(e, "An unexpected error has occurred.");
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        TwitterUrl = profile.TwitterUserName.Select(x => string.IsNullOrWhiteSpace(x) ? null : $"https://twitter.com/{x}")
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        GitHubUrl = profile.GitHubUserName.Select(x => string.IsNullOrWhiteSpace(x) ? null : $"https://github.com/{x}")
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        YouTubeUrl = profile.YouTubeUrl
            .CopyToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        BlogUrl = profile.BlogUrl
            .CopyToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Email = profile.Email
            .CopyToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    public Profile Profile { get; }

    public IReadOnlyReactiveProperty<string?> TwitterUrl { get; }

    public IReadOnlyReactiveProperty<string?> GitHubUrl { get; }

    public IReadOnlyReactiveProperty<string?> YouTubeUrl { get; }

    public IReadOnlyReactiveProperty<string?> BlogUrl { get; }

    public IReadOnlyReactiveProperty<string?> Email { get; }

    public AvaloniaList<Package?> Packages { get; } = new();

    public AsyncReactiveCommand Refresh { get; }

    public AsyncReactiveCommand More { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public override void Dispose()
    {
        _disposables.Dispose();
    }

    private async Task RefreshPackages()
    {
        Packages.Clear();
        Package[] array = await Profile.GetPackagesAsync(0, 30);
        Packages.AddRange(array);

        if (array.Length == 30)
        {
            Packages.Add(null);
        }
    }

    private async Task MoreLoadPackages()
    {
        Packages.RemoveAt(Packages.Count - 1);
        Package[] array = await Profile.GetPackagesAsync(Packages.Count, 30);
        Packages.AddRange(array);

        if (array.Length == 30)
        {
            Packages.Add(null);
        }
    }
}
