
using Google.Cloud.Firestore;

namespace BeUtl.Models.Extensions.Develop;

public interface ILocalizedPackageResource
{
    string? DisplayName { get; }

    string? Description { get; }

    string? ShortDescription { get; }

    ImageLink? LogoImage { get; }

    ImageLink[] Screenshots { get; }

    CultureInfo Culture { get; }

    public interface ILink : ILocalizedPackageResource
    {
        DocumentSnapshot Snapshot { get; }

        IObservable<ILink> GetObservable();

        ValueTask<ILink> RefreshAsync(CancellationToken cancellationToken = default);

        ValueTask<ILink> SyncronizeToAsync(ILocalizedPackageResource value, PackageResourceFields fieldsMask, CancellationToken cancellationToken = default);

        ValueTask PermanentlyDeleteAsync();
    }
}
