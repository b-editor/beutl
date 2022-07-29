using Google.Cloud.Firestore;

namespace BeUtl.Models.Extensions.Develop;

public sealed record LocalizedReleaseResource(
    string Title,
    string Body,
    CultureInfo Culture)
    : ILocalizedReleaseResource;

public sealed class LocalizedReleaseResourceLink : ILocalizedReleaseResource.ILink
{
    public LocalizedReleaseResourceLink(DocumentSnapshot snapshot)
    {
        Snapshot = snapshot;
        Culture = CultureInfo.GetCultureInfo(Snapshot.GetValue<string>("culture"));
        Title = Snapshot.GetValue<string>("title");
        Body = Snapshot.GetValue<string>("body");
    }

    public DocumentSnapshot Snapshot { get; }

    public string Title { get; }

    public string Body { get; }

    public CultureInfo Culture { get; }

    public static async ValueTask<LocalizedReleaseResourceLink> OpenAsync(DocumentReference reference, CancellationToken cancellationToken = default)
    {
        DocumentSnapshot snapshot = await reference.GetSnapshotAsync(cancellationToken);
        return new LocalizedReleaseResourceLink(snapshot);
    }

    public IObservable<ILocalizedReleaseResource.ILink> GetObservable()
    {
        return Snapshot.Reference.ToObservable()
            .Select(snapshot => new LocalizedReleaseResourceLink(snapshot));
    }

    public async ValueTask PermanentlyDeleteAsync()
    {
        await Snapshot.Reference.DeleteAsync();
    }

    public async ValueTask<ILocalizedReleaseResource.ILink> RefreshAsync(CancellationToken cancellationToken = default)
    {
        return await OpenAsync(Snapshot.Reference, cancellationToken);
    }

    public async ValueTask<ILocalizedReleaseResource.ILink> SyncronizeToAsync(ILocalizedReleaseResource value, ReleaseResourceFields fieldsMask, CancellationToken cancellationToken = default)
    {
        DocumentReference reference = Snapshot.Reference;
#pragma warning disable IDE0055
        var dict = new Dictionary<string, string>
        {
            ["title"]   = (fieldsMask.HasFlag(ReleaseResourceFields.Title)   ? this : value).Title,
            ["body"]    = (fieldsMask.HasFlag(ReleaseResourceFields.Body)    ? this : value).Body,
            ["culture"] = (fieldsMask.HasFlag(ReleaseResourceFields.Culture) ? this : value).Culture.Name
        };
#pragma warning restore IDE0055

        cancellationToken.ThrowIfCancellationRequested();

        await reference.SetAsync(dict, SetOptions.Overwrite, cancellationToken: cancellationToken);

        return await OpenAsync(reference, cancellationToken);
    }
}
