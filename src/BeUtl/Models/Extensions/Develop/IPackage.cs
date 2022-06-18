
using Google.Cloud.Firestore;

namespace BeUtl.Models.Extensions.Develop;

public interface IPackage
{
    string DisplayName { get; }

    string Name { get; }

    string Description { get; }

    string ShortDescription { get; }

    bool IsVisible { get; }

    ImageLink? LogoImage { get; }

    ImageLink[] Screenshots { get; }

    public interface ILink : IPackage
    {
        DocumentSnapshot Snapshot { get; }

        IDisposable SubscribeResources(Action<DocumentSnapshot> added, Action<DocumentSnapshot> removed, Action<DocumentSnapshot> modified);

        IObservable<ILink> GetObservable();

        ValueTask<ILink> RefreshAsync(CancellationToken cancellationToken = default);

        ValueTask<ILink> SyncronizeToAsync(IPackage value, PackageInfoFields fieldsMask, CancellationToken cancellationToken = default);

        ValueTask<ILink> ChangeVisibility(bool visibility, CancellationToken cancellationToken = default);

        ValueTask PermanentlyDeleteAsync();
    }
}
