using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using Avalonia.Media.Imaging;

using BeUtl.Collections;
using BeUtl.Models.Extensions.Develop;
using BeUtl.Services;

using Google.Cloud.Firestore;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageDetailsPageViewModel : IDisposable
{
    private readonly PackageController _packageController = ServiceLocator.Current.GetRequiredService<PackageController>();
    private readonly HttpClient _httpClient = ServiceLocator.Current.GetRequiredService<HttpClient>();
    private readonly CompositeDisposable _disposables = new(15);

    public PackageDetailsPageViewModel(DocumentReference docRef, IPackage.ILink package)
    {
        Reference = docRef;
        DisplayCulture.Value = CultureInfo.CurrentUICulture;

        Package = package.GetObservable()
            .ToReadOnlyReactivePropertySlim(package);

        LogoImage = Package
            .Select(p => p.LogoImage)
            .SelectMany(async il => il != null ? await il.TryGetBitmapAsync() : null)
            .ToReadOnlyReactivePropertySlim(null)
            .DisposeWith(_disposables);

        HasLogoImage = LogoImage.Select(i => i != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IsPublic = Package.Select(p => p.IsVisible)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Settings = new PackageSettingsPageViewModel(docRef, this)
            .DisposeWith(_disposables);
        Releases = new PackageReleasesPageViewModel(this)
            .DisposeWith(_disposables);

        IObservable<CoreList<ResourcePageViewModel>> resourceObservable = Settings.Items.ToCollectionChanged<ResourcePageViewModel>()
            .Select(_ => Settings.Items)
            .Publish(Settings.Items)
            .RefCount();

        IObservable<T?> CreateResourceObservable<T>(Func<ResourcePageViewModel, IObservable<T?>> func)
            where T : class
        {
            return Package
                .CombineLatest(resourceObservable, DisplayCulture)
                .SelectMany(t =>
                {
                    foreach (var item in t.Second)
                    {
                        if (item.ActualCulture.Value.Name == t.Third.Name)
                        {
                            return func(item);
                        }
                    }

                    return Observable.Return<T?>(null);
                });
        }

        LocalizedDisplayName = CreateResourceObservable(v => v.ActualDisplayName)
            .CombineLatest(Package)
            .Select(t => t.First ?? t.Second.DisplayName)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        LocalizedDescription = CreateResourceObservable(v => v.ActualDescription)
            .CombineLatest(Package)
            .Select(t => t.First ?? t.Second.Description)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        LocalizedLogoImage = CreateResourceObservable(v => v.ActualLogoImageId)
            .CombineLatest(Package)
            .Select(t => t.First ?? t.Second.LogoImage?.Name)
            .SelectMany(async id => id != null ? await _packageController.GetPackageImageStream(Reference.Id, id) : null)
            .DisposePreviousValue()
            .Select(stream => stream != null ? new Bitmap(stream) : null)
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    public ReadOnlyReactivePropertySlim<IPackage.ILink> Package { get; }

    public PackageSettingsPageViewModel Settings { get; }

    public PackageReleasesPageViewModel Releases { get; }

    public DocumentReference Reference { get; }

    public ReadOnlyReactivePropertySlim<Bitmap?> LogoImage { get; }

    public ReadOnlyReactivePropertySlim<bool> HasLogoImage { get; }

    public ReadOnlyReactivePropertySlim<string?> LocalizedDisplayName { get; }

    public ReadOnlyReactivePropertySlim<string?> LocalizedDescription { get; }

    public ReadOnlyReactivePropertySlim<Bitmap?> LocalizedLogoImage { get; }

    public ReadOnlyReactivePropertySlim<bool> IsPublic { get; }

    public ReactiveProperty<CultureInfo> DisplayCulture { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
