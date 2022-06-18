using Google.Cloud.Firestore;

namespace BeUtl.Models.Extensions.Develop;

public interface IPackageRelease
{
    Version Version { get; }

    string Title { get; }

    string Body { get; }

    bool IsVisible { get; }

    string? DownloadLink { get; }

    string? SHA256 { get; }

    public interface ILink : IPackageRelease
    {
        DocumentSnapshot Snapshot { get; }

        IDisposable SubscribeResources(Action<DocumentSnapshot> added, Action<DocumentSnapshot> removed, Action<DocumentSnapshot> modified);

        IObservable<ILink> GetObservable();

        ValueTask<ILink> RefreshAsync(CancellationToken cancellationToken = default);

        ValueTask<ILink> SyncronizeToAsync(IPackageRelease value, PackageReleaseFields fieldsMask, CancellationToken cancellationToken = default);

        ValueTask<ILocalizedReleaseResource.ILink> AddResource(ILocalizedReleaseResource resource);

        ValueTask<ILink> ChangeVisibility(bool visibility, CancellationToken cancellationToken = default);

        ValueTask PermanentlyDeleteAsync();
    }
}
