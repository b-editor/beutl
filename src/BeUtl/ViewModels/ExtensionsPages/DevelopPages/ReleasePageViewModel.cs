using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using BeUtl.Collections;
using BeUtl.Models.Extensions.Develop;

using Google.Cloud.Firestore;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class ReleaseResourceViewModel
{
    public ReleaseResourceViewModel(DocumentReference reference)
    {
        Reference = reference;
        Delete.Subscribe(() => Reference.DeleteAsync());
    }

    public DocumentReference Reference { get; }

    public ReactivePropertySlim<string> Title { get; } = new();

    public ReactivePropertySlim<string> Body { get; } = new();

    public ReactivePropertySlim<CultureInfo> Culture { get; } = new();

    public ReactiveCommand Delete { get; } = new();

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
    private readonly object _lockObject = new();
    private readonly FirestoreChangeListener? _listener;

    public ReleasePageViewModel(IPackageRelease.ILink release, PackageReleasesPageViewModel parent)
    {
        Parent = parent;
        Release = release;

        VersionInput.SetValidateNotifyError(str => System.Version.TryParse(str, out _) ? null : "CultureNotFoundException");

        Version = VersionInput.Select(str => System.Version.TryParse(str, out Version? v) ? v : null)
            .ToReadOnlyReactivePropertySlim();

        Title.SetValidateNotifyError(NotNullOrWhitespace);
        Body.SetValidateNotifyError(NotNullOrWhitespace);

        IsChanging = Version.CombineLatest(ActualVersion).Select(t => t.First == t.Second)
            .CombineLatest(
                Title.CombineLatest(ActualTitle).Select(t => t.First == t.Second),
                Body.CombineLatest(ActualBody).Select(t => t.First == t.Second))
            .Select(t => !(t.First && t.Second && t.Third))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Save = new AsyncReactiveCommand(Title.ObserveHasErrors
            .CombineLatest(Body.ObserveHasErrors, VersionInput.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third)));

        Save.Subscribe(async () =>
        {
            await Release.SyncronizeToAsync(new PackageRelease(
                ActualVersion.Value = Version.Value!,
                ActualTitle.Value = Title.Value,
                ActualBody.Value = Body.Value,
                false,
                null,
                null),
                PackageReleaseFields.All & ~(PackageReleaseFields.SHA256 | PackageReleaseFields.DownloadLink | PackageReleaseFields.IsVisible));
        });

        DiscardChanges.Subscribe(async () =>
        {
            DocumentSnapshot snapshot = await Release.Snapshot.Reference.GetSnapshotAsync();
            Update(snapshot);
        });

        Delete.Subscribe(async () => await Release.PermanentlyDeleteAsync());

        MakePublic.Subscribe(async () => await Release.ChangeVisibility(true)).DisposeWith(_disposables);

        MakePrivate.Subscribe(async () => await Release.ChangeVisibility(false)).DisposeWith(_disposables);

        release.SubscribeResources(
            item =>
            {
                if (!Items.Any(p => p.Reference.Id == item.Reference.Id))
                {
                    var viewModel = new ReleaseResourceViewModel(item.Reference);
                    viewModel.Update(item);
                    Items.Add(viewModel);
                }
            },
            item =>
            {
                var viewModel = Items.FirstOrDefault(p => p.Reference.Id == item.Reference.Id);
                if (viewModel != null)
                {
                    Items.Remove(viewModel);
                }
            },
            item =>
            {
                var viewModel = Items.FirstOrDefault(p => p.Reference.Id == item.Reference.Id);
                viewModel?.Update(item);
            })
            .DisposeWith(_disposables);
    }

    public PackageReleasesPageViewModel Parent { get; }

    public IPackageRelease.ILink Release { get; }

    public ReactivePropertySlim<string> ActualTitle { get; } = new();

    public ReactivePropertySlim<string> ActualBody { get; } = new();

    public ReactivePropertySlim<Version> ActualVersion { get; } = new();

    public ReactivePropertySlim<bool> IsPublic { get; } = new();

    public ReactiveProperty<string> Title { get; } = new();

    public ReactiveProperty<string> Body { get; } = new();

    public ReactiveProperty<string> VersionInput { get; } = new();

    public ReadOnlyReactivePropertySlim<Version?> Version { get; }

    public ReadOnlyReactivePropertySlim<bool> IsChanging { get; }

    public AsyncReactiveCommand Save { get; }

    public AsyncReactiveCommand DiscardChanges { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public ReactiveCommand MakePublic { get; } = new();

    public ReactiveCommand MakePrivate { get; } = new();

    public CoreList<ReleaseResourceViewModel> Items { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();
        _listener?.StopAsync();

        Items.Clear();
    }

    public void Update(DocumentSnapshot snapshot)
    {
        ActualTitle.Value = Title.Value = snapshot.GetValue<string>("title");
        ActualBody.Value = Body.Value = snapshot.GetValue<string>("body");
        ActualVersion.Value = System.Version.Parse(VersionInput.Value = snapshot.GetValue<string>("version"));
        IsPublic.Value = snapshot.GetValue<bool>("visible");
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
