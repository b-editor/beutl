using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Google.Cloud.Firestore;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageDetailsPageViewModel : IDisposable
{
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

        IsPublic = observable.Select(d => d.GetValue<bool>("visible"))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Settings = new PackageSettingsPageViewModel(docRef, this);
        Releases = new PackageReleasesPageViewModel(this);

        LocalizedDisplayName = Settings.Resources.Items.ToCollectionChanged<ResourcePageViewModel>()
            .SelectMany(_ => Settings.Resources.Items.Count > 0
                ? Settings.Resources.Items.ToObservable()
                    .SelectMany(i => i.ActualCulture
                        .CombineLatest(i.ActualDisplayName)
                        .Select(ii => ii.First.Equals(CultureInfo.CurrentUICulture) ? ii.Second : null))
                : Observable.Return<string?>(null))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        HasLocalizedDisplayName = LocalizedDisplayName
            .Select(i => !string.IsNullOrWhiteSpace(i))
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

    public ReadOnlyReactivePropertySlim<string?> LocalizedDisplayName { get; }

    public ReadOnlyReactivePropertySlim<bool> HasLocalizedDisplayName { get; }

    public ReadOnlyReactivePropertySlim<bool> IsPublic { get; }

    public void Dispose()
    {
        _disposables.Dispose();
        Settings.Dispose();
    }
}
