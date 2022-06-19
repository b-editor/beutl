using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using BeUtl.Collections;
using BeUtl.Models.Extensions.Develop;

using Google.Cloud.Firestore;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class ReleaseResourceViewModel
{
    public ReleaseResourceViewModel(ILocalizedReleaseResource.ILink resource)
    {
        Resource = resource;
        Delete.Subscribe(async () => await Resource.PermanentlyDeleteAsync());
    }

    public ILocalizedReleaseResource.ILink Resource { get; }

    public ReactivePropertySlim<string> Title { get; } = new();

    public ReactivePropertySlim<string> Body { get; } = new();

    public ReactivePropertySlim<CultureInfo> Culture { get; } = new();

    public AsyncReactiveCommand Delete { get; } = new();

    public void Update(DocumentSnapshot snapshot)
    {
        Title.Value = snapshot.GetValue<string>("title");
        Body.Value = snapshot.GetValue<string>("body");
        Culture.Value = CultureInfo.GetCultureInfo(snapshot.GetValue<string>("culture"));
    }
}

public sealed class ReleasePageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public ReleasePageViewModel(IPackageRelease.ILink release, PackageReleasesPageViewModel parent)
    {
        Parent = parent;
        Release = release.GetObservable()
            .ToReadOnlyReactivePropertySlim(release)
            .DisposeWith(_disposables);

        Title = Release.Select(x => x.Title)
            .ToReactiveProperty(release.Title)
            .DisposeWith(_disposables);
        Body = Release.Select(x => x.Body)
            .ToReactiveProperty(release.Body)
            .DisposeWith(_disposables);
        VersionInput = Release.Select(x => x.Version.ToString())
            .ToReactiveProperty(release.Version.ToString())
            .DisposeWith(_disposables);

        VersionInput.SetValidateNotifyError(str => System.Version.TryParse(str, out _) ? null : "CultureNotFoundException");

        Version = VersionInput.Select(str => System.Version.TryParse(str, out Version? v) ? v : null)
            .ToReadOnlyReactivePropertySlim();
        Title.SetValidateNotifyError(NotNullOrWhitespace);
        Body.SetValidateNotifyError(NotNullOrWhitespace);

        IsChanging = Version.CombineLatest(Release).Select(t => t.First == t.Second.Version)
            .CombineLatest(
                Title.CombineLatest(Release).Select(t => t.First == t.Second.Title),
                Body.CombineLatest(Release).Select(t => t.First == t.Second.Body))
            .Select(t => !(t.First && t.Second && t.Third))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Save = new AsyncReactiveCommand(Title.ObserveHasErrors
            .CombineLatest(Body.ObserveHasErrors, VersionInput.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third)));

        Save.Subscribe(async () =>
        {
            await Release.Value.SyncronizeToAsync(new PackageRelease(
                Version: Version.Value!,
                Title: Title.Value,
                Body: Body.Value,
                IsVisible: false,
                DownloadLink: null,
                SHA256: null),
                PackageReleaseFields.All & ~(PackageReleaseFields.SHA256 | PackageReleaseFields.DownloadLink | PackageReleaseFields.IsVisible));
        });

        DiscardChanges.Subscribe(() =>
        {
            Title.Value = Release.Value.Title;
            Body.Value = Release.Value.Body;
            VersionInput.Value = Release.Value.Version.ToString();
        });

        Delete.Subscribe(async () => await Release.Value.PermanentlyDeleteAsync());

        MakePublic.Subscribe(async () => await Release.Value.ChangeVisibility(true)).DisposeWith(_disposables);

        MakePrivate.Subscribe(async () => await Release.Value.ChangeVisibility(false)).DisposeWith(_disposables);

        release.SubscribeResources(
            item =>
            {
                if (!Items.Any(p => p.Resource.Snapshot.Id == item.Reference.Id))
                {
                    var viewModel = new ReleaseResourceViewModel(new LocalizedReleaseResourceLink(item));
                    viewModel.Update(item);
                    Items.Add(viewModel);
                }
            },
            item =>
            {
                ReleaseResourceViewModel? viewModel = Items.FirstOrDefault(p => p.Resource.Snapshot.Id == item.Reference.Id);
                if (viewModel != null)
                {
                    Items.Remove(viewModel);
                }
            },
            item =>
            {
                ReleaseResourceViewModel? viewModel = Items.FirstOrDefault(p => p.Resource.Snapshot.Id == item.Reference.Id);
                viewModel?.Update(item);
            })
            .DisposeWith(_disposables);
    }

    public PackageReleasesPageViewModel Parent { get; }

    public ReadOnlyReactivePropertySlim<IPackageRelease.ILink> Release { get; }

    public ReactiveProperty<string> Title { get; }

    public ReactiveProperty<string> Body { get; }

    public ReactiveProperty<string> VersionInput { get; }

    public ReadOnlyReactivePropertySlim<Version?> Version { get; }

    public ReadOnlyReactivePropertySlim<bool> IsChanging { get; }

    public AsyncReactiveCommand Save { get; }

    public ReactiveCommand DiscardChanges { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public ReactiveCommand MakePublic { get; } = new();

    public ReactiveCommand MakePrivate { get; } = new();

    public CoreList<ReleaseResourceViewModel> Items { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();

        Items.Clear();
    }

    private static string NotNullOrWhitespace(string str)
    {
        if (!string.IsNullOrWhiteSpace(str))
        {
            return null!;
        }
        else
        {
            return "Please enter a string.";
        }
    }
}
