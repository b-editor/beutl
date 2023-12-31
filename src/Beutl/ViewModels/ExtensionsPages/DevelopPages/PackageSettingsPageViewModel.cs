using System.Reactive.Concurrency;

using Avalonia.Collections;

using Beutl.Api.Objects;
using Beutl.ViewModels.Dialogs;

using OpenTelemetry.Trace;

using Reactive.Bindings;

using Serilog;

namespace Beutl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageSettingsPageViewModel : BasePageViewModel, ISupportRefreshViewModel
{
    private readonly ILogger _logger = Log.ForContext<PackageSettingsPageViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly AuthorizedUser _user;
    private readonly ReactivePropertySlim<bool> _screenshotsChange = new();

    public PackageSettingsPageViewModel(AuthorizedUser user, Package package)
    {
        _user = user;
        Package = package;

        ActualLogo = package.LogoId
            .ObserveOn(TaskPoolScheduler.Default)
            .SelectMany(async id =>
            {
                try
                {
                    IsLogoLoading.Value = true;
                    using (await _user.Lock.LockAsync())
                    {
                        await _user.RefreshAsync();
                        return id.HasValue ? await _user.Profile.GetAssetAsync(id.Value) : null;
                    }
                }
                finally
                {
                    IsLogoLoading.Value = false;
                }
            })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        DisplayName = Package.DisplayName
            .CopyToReactiveProperty()
            .DisposeWith(_disposables);
        Description = Package.Description
            .CopyToReactiveProperty()
            .DisposeWith(_disposables);
        ShortDescription = Package.ShortDescription
            .CopyToReactiveProperty()
            .DisposeWith(_disposables);
        Logo = ActualLogo
            .CopyToReactiveProperty()
            .DisposeWith(_disposables);

        Screenshots = [];
        package.Screenshots.Subscribe(async x => await ResetScreenshots(x))
            .DisposeWith(_disposables);

        // 値が変更されるか
        IsChanging = DisplayName.EqualTo(Package.DisplayName)
            .AreTrue(
                Description.EqualTo(Package.Description),
                ShortDescription.EqualTo(Package.ShortDescription),
                Logo.EqualTo(ActualLogo, (x, y) => x?.Id == y?.Id),
                _screenshotsChange.Not())
            .Not()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        // コマンドを初期化
        Save = new AsyncReactiveCommand()
            .WithSubscribe(async () =>
            {
                using Activity? activity = Services.Telemetry.StartActivity("PackageSettingsPage.Save");

                try
                {
                    using (await _user.Lock.LockAsync())
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));

                        await _user.RefreshAsync();

                        await Package.UpdateAsync(
                            description: Description.Value,
                            displayName: DisplayName.Value,
                            shortDescription: ShortDescription.Value,
                            logoImageId: Logo.Value?.Id,
                            screenshots: Screenshots.Select(x => x.Id).ToArray());
                    }
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    activity?.RecordException(ex);
                    ErrorHandle(ex);
                    _logger.Error(ex, "An unexpected error has occurred.");
                }
            })
            .DisposeWith(_disposables);

        DiscardChanges = new AsyncReactiveCommand()
            .WithSubscribe(async () =>
            {
                DisplayName.Value = Package.DisplayName.Value;
                Description.Value = Package.Description.Value;
                ShortDescription.Value = Package.ShortDescription.Value;
                Logo.Value = ActualLogo.Value;
                await ResetScreenshots(Package.Screenshots.Value);
            })
            .DisposeWith(_disposables);

        Delete = new AsyncReactiveCommand()
            .WithSubscribe(async () =>
            {
                using Activity? activity = Services.Telemetry.StartActivity("PackageSettingsPage.Delete");

                try
                {
                    using (await _user.Lock.LockAsync())
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));

                        await _user.RefreshAsync();

                        await Package.DeleteAsync();
                    }
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    activity?.RecordException(ex);
                    ErrorHandle(ex);
                    _logger.Error(ex, "An unexpected error has occurred.");
                }
            })
            .DisposeWith(_disposables);

        MakePublic = new AsyncReactiveCommand()
            .WithSubscribe(async () => await SetVisibility(true))
            .DisposeWith(_disposables);

        MakePrivate = new AsyncReactiveCommand()
            .WithSubscribe(async () => await SetVisibility(false))
            .DisposeWith(_disposables);

        Refresh = new AsyncReactiveCommand()
            .WithSubscribe(async () =>
            {
                using Activity? activity = Services.Telemetry.StartActivity("PackageSettingsPage.Refresh");

                try
                {
                    using (await _user.Lock.LockAsync())
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));

                        IsBusy.Value = true;
                        await _user.RefreshAsync();

                        await Package.RefreshAsync();
                    }
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    activity?.RecordException(ex);
                    ErrorHandle(ex);
                    _logger.Error(ex, "An unexpected error has occurred.");
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        AddScreenshot = new ReactiveCommand<Asset>()
            .WithSubscribe(item =>
            {
                if (!Screenshots.Contains(item))
                {
                    Screenshots.Insert(0, item);
                    _screenshotsChange.Value = true;
                }
            })
            .DisposeWith(_disposables);

        DeleteScreenshot = new ReactiveCommand<Asset>()
            .WithSubscribe(item =>
            {
                Screenshots.Remove(item);
                _screenshotsChange.Value = true;
            })
            .DisposeWith(_disposables);

        MoveScreenshotFront = new ReactiveCommand<Asset>()
            .WithSubscribe(item =>
            {
                int oldIndex = Screenshots.IndexOf(item);
                int newIndex = Math.Max(oldIndex - 1, 0);
                Screenshots.Move(oldIndex, newIndex);
            })
            .DisposeWith(_disposables);

        MoveScreenshotBack = new ReactiveCommand<Asset>()
            .WithSubscribe(item =>
            {
                int oldIndex = Screenshots.IndexOf(item);
                int newIndex = Math.Min(oldIndex + 1, Screenshots.Count - 1);
                Screenshots.Move(oldIndex, newIndex);
            })
            .DisposeWith(_disposables);

        Refresh.Execute();
    }

    public Package Package { get; }

    public ReadOnlyReactivePropertySlim<bool> IsChanging { get; }

    public ReactiveProperty<string?> DisplayName { get; } = new();

    public ReactiveProperty<string?> Description { get; } = new();

    public ReactiveProperty<string?> ShortDescription { get; } = new();

    public ReadOnlyReactivePropertySlim<Asset?> ActualLogo { get; }

    public ReactiveProperty<Asset?> Logo { get; }

    public AvaloniaList<Asset> Screenshots { get; }

    public AsyncReactiveCommand Save { get; }

    public AsyncReactiveCommand DiscardChanges { get; }

    public AsyncReactiveCommand Delete { get; }

    public AsyncReactiveCommand MakePublic { get; }

    public AsyncReactiveCommand MakePrivate { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public ReactivePropertySlim<bool> IsLogoLoading { get; } = new();

    public AsyncReactiveCommand Refresh { get; }

    public ReactiveCommand<Asset> AddScreenshot { get; }

    public ReactiveCommand<Asset> DeleteScreenshot { get; }

    public ReactiveCommand<Asset> MoveScreenshotFront { get; }

    public ReactiveCommand<Asset> MoveScreenshotBack { get; }

    public override void Dispose()
    {
        _disposables.Dispose();
    }

    public SelectImageAssetViewModel SelectImageAssetViewModel()
    {
        return new SelectImageAssetViewModel(_user);
    }

    private async ValueTask ResetScreenshots(IDictionary<string, string>? items)
    {
        using (await _user.Lock.LockAsync())
        {
            Screenshots.Clear();
            if (items != null)
            {
                foreach ((string key, string _) in items)
                {
                    long id = long.Parse(key);
                    Screenshots.Add(await Package.Owner.GetAssetAsync(id));
                }
            }

            _screenshotsChange.Value = false;
        }
    }

    private async Task SetVisibility(bool isPublic)
    {
        using Activity? activity = Services.Telemetry.StartActivity("PackageSettingsPage.SetVisibility");

        try
        {
            using (await _user.Lock.LockAsync())
            {
                activity?.AddEvent(new("Entered_AsyncLock"));

                await _user.RefreshAsync();

                await Package.UpdateAsync(isPublic: isPublic);
            }
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.RecordException(ex);
            ErrorHandle(ex);
            _logger.Error(ex, "An unexpected error has occurred.");
        }
    }
}
