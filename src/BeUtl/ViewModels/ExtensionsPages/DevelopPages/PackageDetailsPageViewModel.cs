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
    private readonly CompositeDisposable _disposables = new();

    public PackageDetailsPageViewModel(DocumentReference docRef)
    {
        Reference = docRef;

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
            .ToReadOnlyReactivePropertySlim(mode: ReactivePropertyMode.RaiseLatestValueOnSubscribe)
            .DisposeWith(_disposables);

        Logo = LogoId.Select(id => id != null ? _packageController.GetPackageImageRef(Reference.Id, id) : null)
            .SelectMany(r => r?.GetDownloadUrlAsync() ?? Task.FromResult<string?>(null))
            .Select(s => s != null ? new Uri(s) : null)
            .ToReadOnlyReactivePropertySlim(null)
            .DisposeWith(_disposables);

        LogoImage = Logo.SelectMany(async uri => uri != null ? await _httpClient.GetByteArrayAsync(uri) : null)
            .Select(arr => arr != null ? new MemoryStream(arr) : null)
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

        Settings = new PackageSettingsPageViewModel(docRef, this);
        Releases = new PackageReleasesPageViewModel(this);

        IObservable<CollectionChanged<ResourcePageViewModel>> resourceObservable = Settings.Items.ToCollectionChanged<ResourcePageViewModel>();
        LocalizedDisplayName = observable
            .SelectMany(_ => Settings.Items.Count > 0
                ? Settings.Items.ToObservable()
                    .SelectMany(i => i.ActualCulture
                        .CombineLatest(i.ActualDisplayName)
                        .Select(ii => ii.First.Equals(CultureInfo.CurrentUICulture) ? ii.Second : null))
                : Observable.Return<string?>(null))
            .CombineLatest(DisplayName)
            .Select(t => t.First ?? t.Second)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        HasLocalizedDisplayName = LocalizedDisplayName
            .CombineLatest(DisplayName)
            .Select(t => t.First != t.Second)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        LocalizedDescription = observable
            .SelectMany(_ => Settings.Items.Count > 0
                ? Settings.Items.ToObservable()
                    .SelectMany(i => i.ActualCulture
                        .CombineLatest(i.ActualDescription)
                        .Select(ii => ii.First.Equals(CultureInfo.CurrentUICulture) ? ii.Second : null))
                : Observable.Return<string?>(null))
            .CombineLatest(Description)
            .Select(t => t.First ?? t.Second)
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

    public ReadOnlyReactivePropertySlim<Uri?> Logo { get; }

    public ReadOnlyReactivePropertySlim<Bitmap?> LogoImage { get; }

    public ReadOnlyReactivePropertySlim<bool> HasLogoImage { get; }

    public ReadOnlyReactivePropertySlim<string?> LocalizedDisplayName { get; }

    public ReadOnlyReactivePropertySlim<string?> LocalizedDescription { get; }

    public ReadOnlyReactivePropertySlim<bool> HasLocalizedDisplayName { get; }

    public ReadOnlyReactivePropertySlim<bool> IsPublic { get; }

    public void Dispose()
    {
        _disposables.Dispose();
        Settings.Dispose();
    }
}
