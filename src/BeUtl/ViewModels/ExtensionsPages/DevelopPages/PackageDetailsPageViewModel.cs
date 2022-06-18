using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia.Media.Imaging;

using BeUtl.Collections;
using BeUtl.Models.Extensions.Develop;

using Google.Cloud.Firestore;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageDetailsPageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new(15);

    public PackageDetailsPageViewModel(DocumentReference docRef, IPackage.ILink package)
    {
        Reference = docRef;
        DisplayCulture.Value = CultureInfo.CurrentUICulture;

        Package = package.GetObservable()
            .ToReadOnlyReactivePropertySlim(package);

        Settings = new PackageSettingsPageViewModel(docRef, this)
            .DisposeWith(_disposables);
        Releases = new PackageReleasesPageViewModel(this)
            .DisposeWith(_disposables);

        IObservable<CoreList<ResourcePageViewModel>> resourceObservable = Settings.Items.ToCollectionChanged<ResourcePageViewModel>()
            .Select(_ => Settings.Items)
            .Publish(Settings.Items)
            .RefCount();

        IObservable<T?> CreateResourceObservable<T>(Func<ILocalizedPackageResource, T?> func)
            where T : class
        {
            return Package
                .CombineLatest(resourceObservable, DisplayCulture)
                .SelectMany(t =>
                {
                    foreach (var item in t.Second)
                    {
                        if (item.Resource.Value.Culture.Name == t.Third.Name)
                        {
                            return item.Resource.Select(func);
                        }
                    }

                    return Observable.Return<T?>(null);
                });
        }

        LocalizedDisplayName = CreateResourceObservable(v => v.DisplayName)
            .CombineLatest(Package)
            .Select(t => t.First ?? t.Second.DisplayName)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        LocalizedDescription = CreateResourceObservable(v => v.Description)
            .CombineLatest(Package)
            .Select(t => t.First ?? t.Second.Description)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        LocalizedLogoImage = CreateResourceObservable(v => v.LogoImage)
            .CombineLatest(Package)
            .Select(t => t.First ?? t.Second.LogoImage)
            .SelectMany(async link => link != null ? await link.TryGetBitmapAsync() : null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        HasLogoImage = LocalizedLogoImage.Select(i => i != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    public ReadOnlyReactivePropertySlim<IPackage.ILink> Package { get; }

    public PackageSettingsPageViewModel Settings { get; }

    public PackageReleasesPageViewModel Releases { get; }

    public DocumentReference Reference { get; }

    public ReadOnlyReactivePropertySlim<bool> HasLogoImage { get; }

    public ReadOnlyReactivePropertySlim<string?> LocalizedDisplayName { get; }

    public ReadOnlyReactivePropertySlim<string?> LocalizedDescription { get; }

    public ReadOnlyReactivePropertySlim<Bitmap?> LocalizedLogoImage { get; }

    public ReactiveProperty<CultureInfo> DisplayCulture { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
