using System.Globalization;

using Google.Cloud.Firestore;

namespace BeUtl.Models.Extensions.Develop;

public interface ILocalizedReleaseResource
{
    string? Title { get; }

    string? Body { get; }

    CultureInfo Culture { get; }

    public interface ILink : ILocalizedReleaseResource
    {
        DocumentSnapshot Snapshot { get; }

        IObservable<ILink> GetObservable();

        ValueTask<ILink> RefreshAsync(CancellationToken cancellationToken = default);

        ValueTask<ILink> SyncronizeToAsync(ILocalizedReleaseResource value, ReleaseResourceFields fieldsMask, CancellationToken cancellationToken = default);

        ValueTask PermanentlyDeleteAsync();
    }
}
