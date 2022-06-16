using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia.Media.Imaging;

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

    public PackageDetailsPageViewModel(DocumentReference docRef)
    {
        Reference = docRef;
        DisplayCulture.Value = CultureInfo.CurrentUICulture;

        IObservable<DocumentSnapshot> observable = Reference.ToObservable();

        Name = observable.Select(d => d.GetValue<string>("name"))
            .ToReadOnlyReactivePropertySlim("")
            .DisposeWith(_disposables);

        DisplayName = observable.Select(d => d.GetValue<string>("displayName"))
            .ToReadOnlyReactivePropertySlim("")
            .DisposeWith(_disposables);

        Description = observable.Select(d => d.GetValue<string>("description"))
            .ToReadOnlyReactivePropertySlim("")
            .DisposeWith(_disposables);

        ShortDescription = observable.Select(d => d.GetValue<string>("shortDescription"))
            .ToReadOnlyReactivePropertySlim("")
            .DisposeWith(_disposables);

        LogoId = observable.Select(d => d.TryGetValue("logo", out string val) ? val : null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Screenshots = observable.Select(d => d.TryGetValue("screenshots", out string[] val) ? val : Array.Empty<string>())
            .ToReadOnlyReactivePropertySlim(Array.Empty<string>())
            .DisposeWith(_disposables);

        LogoImage = LogoId
            .SelectMany(id => _packageController.GetPackageImageStream(Reference.Id, id))
            .Select(st => (st, st != null ? new Bitmap(st) : null))
            .Do(t => t.st?.Dispose())
            .Select(t => t.Item2)
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim(null)
            .DisposeWith(_disposables);

        HasLogoImage = LogoImage.Select(i => i != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IsPublic = observable.Select(d => d.GetValue<bool>("visible"))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Settings = new PackageSettingsPageViewModel(docRef, this)
            .DisposeWith(_disposables);
        Releases = new PackageReleasesPageViewModel(this)
            .DisposeWith(_disposables);

        IObservable<CollectionChanged<ResourcePageViewModel>> resourceObservable = Settings.Items.ToCollectionChanged<ResourcePageViewModel>();

        IObservable<T?> CreateResourceObservable<T>(Func<ResourcePageViewModel, IObservable<T?>> func)
            where T : class
        {
            return observable
                .CombineLatest(resourceObservable)
                .SelectMany(_ => Settings.Items.Count > 0
                    ? Settings.Items.ToObservable()
                        .SelectMany(i => i.ActualCulture
                            .CombineLatest(func(i), DisplayCulture)
                            .Select(ii => ii.First?.Name == ii.Third?.Name ? ii.Second : null))
                    : Observable.Return<T?>(null));
        }

        LocalizedDisplayName = CreateResourceObservable(v => v.ActualDisplayName)
            .CombineLatest(DisplayName)
            .Select(t => t.First ?? t.Second)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        HasLocalizedDisplayName = LocalizedDisplayName
            .CombineLatest(DisplayName)
            .Select(t => t.First != t.Second)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        LocalizedDescription = CreateResourceObservable(v => v.ActualDescription)
            .CombineLatest(Description)
            .Select(t => t.First ?? t.Second)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        LocalizedLogoImage = CreateResourceObservable(v => v.ActualLogoImageId)
            .CombineLatest(LogoId)
            .Select(t => t.First ?? t.Second)
            .SelectMany(async id => id != null ? await _packageController.GetPackageImageStream(Reference.Id, id) : null)
            .DisposePreviousValue()
            .Select(stream => stream != null ? new Bitmap(stream) : null)
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    public PackageSettingsPageViewModel Settings { get; }

    public PackageReleasesPageViewModel Releases { get; }

    public DocumentReference Reference { get; }

    public ReadOnlyReactivePropertySlim<string> Name { get; }

    public ReadOnlyReactivePropertySlim<string> DisplayName { get; }

    public ReadOnlyReactivePropertySlim<string> Description { get; }

    public ReadOnlyReactivePropertySlim<string> ShortDescription { get; }

    public ReadOnlyReactivePropertySlim<string?> LogoId { get; }

    public ReadOnlyReactivePropertySlim<Bitmap?> LogoImage { get; }

    public ReadOnlyReactivePropertySlim<string[]> Screenshots { get; }

    public ReadOnlyReactivePropertySlim<bool> HasLogoImage { get; }

    public ReadOnlyReactivePropertySlim<string?> LocalizedDisplayName { get; }

    public ReadOnlyReactivePropertySlim<string?> LocalizedDescription { get; }

    public ReadOnlyReactivePropertySlim<Bitmap?> LocalizedLogoImage { get; }

    public ReadOnlyReactivePropertySlim<bool> HasLocalizedDisplayName { get; }

    public ReadOnlyReactivePropertySlim<bool> IsPublic { get; }

    public ReactiveProperty<CultureInfo> DisplayCulture { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
