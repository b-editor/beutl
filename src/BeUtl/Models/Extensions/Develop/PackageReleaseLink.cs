using Google.Cloud.Firestore;

namespace BeUtl.Models.Extensions.Develop;

public sealed record PackageRelease(
    Version Version,
    string Title,
    string Body,
    bool IsVisible,
    string? DownloadLink,
    string? SHA256)
    : IPackageRelease;

public sealed class PackageReleaseLink : IPackageRelease.ILink
{
    public PackageReleaseLink(DocumentSnapshot snapshot)
    {
        Snapshot = snapshot;
        Version = Version.Parse(Snapshot.GetValue<string>("version"));
        Title = Snapshot.GetValue<string>("title");
        Body = Snapshot.GetValue<string>("body");
        IsVisible = Snapshot.GetValue<bool>("visible");
        DownloadLink = Snapshot.TryGetValue("downloadLink", out string? downloadLink) ? downloadLink : null;
        SHA256 = Snapshot.TryGetValue("sha256", out string? sha256) ? sha256 : null;
    }

    public DocumentSnapshot Snapshot { get; }

    public Version Version { get; }

    public string Title { get; }

    public string Body { get; }

    public bool IsVisible { get; }

    public string? DownloadLink { get; }

    public string? SHA256 { get; }

    public static async ValueTask<PackageReleaseLink> OpenAsync(DocumentReference reference, CancellationToken cancellationToken = default)
    {
        DocumentSnapshot snapshot = await reference.GetSnapshotAsync(cancellationToken);
        return new PackageReleaseLink(snapshot);
    }

    public async ValueTask<ILocalizedReleaseResource.ILink> AddResource(ILocalizedReleaseResource resource)
    {
        var dict = new Dictionary<string, object?>();
        CollectionReference resources = Snapshot.Reference.Collection("resources");

        dict["culture"] = resource.Culture.Name;
        if (resource.Title != null)
        {
            dict["title"] = resource.Title;
        }
        if (resource.Body != null)
        {
            dict["body"] = resource.Body;
        }

        DocumentReference docRef = await resources.AddAsync(dict);
        return await LocalizedReleaseResourceLink.OpenAsync(docRef);
    }

    public async ValueTask<IPackageRelease.ILink> ChangeVisibility(bool visibility, CancellationToken cancellationToken = default)
    {
        DocumentReference reference = Snapshot.Reference;

        await reference.UpdateAsync("visible", visibility, cancellationToken: cancellationToken);

        return await OpenAsync(reference, cancellationToken);
    }

    public IObservable<IPackageRelease.ILink> GetObservable()
    {
        return Snapshot.Reference.ToObservable()
            .Where(snapshot => snapshot.UpdateTime != Snapshot.UpdateTime)
            .Select(snapshot => new PackageReleaseLink(snapshot));
    }

    public async ValueTask<ILocalizedReleaseResource.ILink[]> GetResources()
    {
        QuerySnapshot collection = await Snapshot.Reference.Collection("resources").GetSnapshotAsync();

        var array = new ILocalizedReleaseResource.ILink[collection.Count];
        for (int i = 0; i < collection.Count; i++)
        {
            DocumentSnapshot item = collection[i];
            array[i] = new LocalizedReleaseResourceLink(item);
        }

        return array;
    }

    public async ValueTask PermanentlyDeleteAsync()
    {
        DocumentReference reference = Snapshot.Reference;
        ILocalizedReleaseResource.ILink[] resources = await GetResources();

        await reference.DeleteAsync();

        foreach (ILocalizedReleaseResource.ILink item in resources)
        {
            await item.PermanentlyDeleteAsync();
        }
    }

    public async ValueTask<IPackageRelease.ILink> RefreshAsync(CancellationToken cancellationToken = default)
    {
        return await OpenAsync(Snapshot.Reference, cancellationToken);
    }

    public IDisposable SubscribeResources(Action<DocumentSnapshot> added, Action<DocumentSnapshot> removed, Action<DocumentSnapshot> modified)
    {
        object lockObject = new();
        CollectionReference collection = Snapshot.Reference.Collection("resources");
        FirestoreChangeListener listener = collection.Listen(snapshot =>
        {
            foreach (DocumentChange item in snapshot.Changes)
            {
                lock (lockObject)
                {
                    if (item.ChangeType == DocumentChange.Type.Added)
                    {
                        added(item.Document);
                    }
                    else if (item.ChangeType == DocumentChange.Type.Removed)
                    {
                        removed(item.Document);
                    }
                    else if (item.ChangeType == DocumentChange.Type.Modified)
                    {
                        modified(item.Document);
                    }
                }
            }
        });

        _ = collection.GetSnapshotAsync();

        return Disposable.Create(listener, async listener => await listener.StopAsync());
    }

    public async ValueTask<IPackageRelease.ILink> SyncronizeToAsync(IPackageRelease value, PackageReleaseFields fieldsMask, CancellationToken cancellationToken = default)
    {
        DocumentReference reference = Snapshot.Reference;
        var dict = new Dictionary<string, object>();
        if (!fieldsMask.HasFlag(PackageReleaseFields.Version))
        {
            dict["version"] = value.Version.ToString();
        }
        if (!fieldsMask.HasFlag(PackageReleaseFields.Title))
        {
            dict["title"] = value.Title;
        }
        if (!fieldsMask.HasFlag(PackageReleaseFields.Body))
        {
            dict["body"] = value.Body;
        }
        if (!fieldsMask.HasFlag(PackageReleaseFields.IsVisible))
        {
            dict["visible"] = value.IsVisible;
        }
        if (!fieldsMask.HasFlag(PackageReleaseFields.DownloadLink) && value.DownloadLink != null)
        {
            dict["downloadLink"] = value.DownloadLink;
        }
        if (!fieldsMask.HasFlag(PackageReleaseFields.SHA256) && value.SHA256 != null)
        {
            dict["sha256"] = value.SHA256;
        }

        cancellationToken.ThrowIfCancellationRequested();

        await reference.UpdateAsync(dict, cancellationToken: cancellationToken);

        return await OpenAsync(reference, cancellationToken);
    }
}
