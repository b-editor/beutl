using BeUtl.Models.Extensions.Develop;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageReleasesPageViewModel : IDisposable
{
    private readonly IDisposable _disposable;
    private readonly WeakReference<PackageDetailsPageViewModel> _parentWeak;

    public PackageReleasesPageViewModel(PackageDetailsPageViewModel parent)
    {
        _parentWeak = new WeakReference<PackageDetailsPageViewModel>(parent);

        _disposable = parent.Package.Value.SubscribeReleases(
            item =>
            {
                if (!Items.Any(p => p.Release.Value.Snapshot.Id == item.Id))
                {
                    var viewModel = new ReleasePageViewModel(new PackageReleaseLink(item), this);
                    Items.Add(viewModel);
                }
            },
            item =>
            {
                ReleasePageViewModel? viewModel = Items.FirstOrDefault(p => p.Release.Value.Snapshot.Id == item.Id);
                if (viewModel != null)
                {
                    Items.Remove(viewModel);
                    viewModel.Dispose();
                }
            },
            _ => { });

        Add.Subscribe(async p =>
        {
            await Parent.Package.Value.AddRelease(new PackageRelease(
                Version: Version.Parse(p.Version.Value),
                Title: p.Title.Value,
                Body: p.Body.Value,
                IsVisible: false,
                DownloadLink: null,
                SHA256: null));
        });
    }

    ~PackageReleasesPageViewModel()
    {
        Dispose();
    }

    public PackageDetailsPageViewModel Parent
        => _parentWeak.TryGetTarget(out PackageDetailsPageViewModel? parent)
            ? parent
            : null!;

    public CoreList<ReleasePageViewModel> Items { get; } = new();

    public AsyncReactiveCommand<AddReleaseDialogViewModel> Add { get; } = new();

    public void Dispose()
    {
        _disposable.Dispose();

        foreach (ReleasePageViewModel item in Items)
        {
            item.Dispose();
        }
        Items.Clear();

        GC.SuppressFinalize(this);
    }
}
