using Avalonia.Media.Imaging;

using Beutl.Api.Objects;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageDetailsPageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly ReactivePropertySlim<PackageResource[]?> _resources = new();
    private readonly Task _initTask;
    private readonly AuthorizedUser _user;

    public PackageDetailsPageViewModel(AuthorizedUser user, Package package)
    {
        _user = user;
        Package = package;

        DisplayCulture.Value = CultureInfo.CurrentUICulture;

        Settings = new PackageSettingsPageViewModel(user, package);
        Releases = new PackageReleasesPageViewModel(user, package);
        _initTask = Task.Run(async () => _resources.Value = await package.GetResourcesAsync());

        IObservable<T?> CreateResourceObservable<T>(Func<PackageResource, IObservable<T?>> func)
            where T : class
        {
            return DisplayCulture
                .CombineLatest(_resources)
                .SelectMany(t =>
                {
                    if (t.Second != null)
                    {
                        foreach (PackageResource item in t.Second)
                        {
                            if (item.Locale.Name == t.First.Name)
                            {
                                return func(item);
                            }
                        }
                    }

                    return Observable.Return<T?>(null);
                });
        }

        LocalizedDisplayName = CreateResourceObservable(v => v.DisplayName)
            .CombineLatest(Package.DisplayName)
            .Select(t => t.First ?? t.Second)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        LocalizedDescription = CreateResourceObservable(v => v.Description)
            .CombineLatest(Package.Description)
            .Select(t => t.First ?? t.Second)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        //LocalizedLogoImage = CreateResourceObservable(v => v.LogoImage)
        //    .CombineLatest(Package)
        //    .Select(t => t.First ?? t.Second)
        //    .SelectMany(async link => link != null ? await link.TryGetBitmapAsync() : null)
        //    .ToReadOnlyReactivePropertySlim()
        //    .DisposeWith(_disposables);
        LocalizedLogoImage = Observable.Return<Bitmap?>(null).ToReadOnlyReactivePropertySlim();

        HasLogoImage = LocalizedLogoImage.Select(i => i != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    ~PackageDetailsPageViewModel()
    {
        Dispose();
    }

    public Package Package { get; }

    public PackageSettingsPageViewModel Settings { get; }

    public PackageReleasesPageViewModel Releases { get; }

    public ReadOnlyReactivePropertySlim<bool> HasLogoImage { get; }

    public ReadOnlyReactivePropertySlim<string?> LocalizedDisplayName { get; }

    public ReadOnlyReactivePropertySlim<string?> LocalizedDescription { get; }

    public ReadOnlyReactivePropertySlim<Bitmap?> LocalizedLogoImage { get; }

    public ReactiveProperty<CultureInfo> DisplayCulture { get; } = new();

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public PackageSettingsPageViewModel CreatePackageSettingsPage()
    {
        return new PackageSettingsPageViewModel(_user, Package);
    }

    public PackageReleasesPageViewModel CreatePackageReleasesPage()
    {
        return new PackageReleasesPageViewModel(_user, Package);
    }

    public async Task RefreshAsync()
    {
        if (IsBusy.Value)
            return;

        try
        {
            IsBusy.Value = true;
            await _initTask;

            await _user.RefreshAsync();

            await Package.RefreshAsync();
            _resources.Value = await Package.GetResourcesAsync();
        }
        catch
        {
            // Todo:
        }
        finally
        {
            IsBusy.Value = false;
        }
    }

    public void Dispose()
    {
        Debug.WriteLine($"{GetType().Name} disposed (Count: {_disposables.Count}).");
        _disposables.Dispose();

        GC.SuppressFinalize(this);
    }
}
