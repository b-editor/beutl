
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Google.Cloud.Firestore;

namespace BeUtl.Models.Extensions.Develop;

public class PackageLink : IPackage.ILink
{
    public PackageLink(DocumentSnapshot snapshot)
    {
        Snapshot = snapshot;
        string imagesPath = $"users/{snapshot.Reference.Parent.Parent.Id}/packages/{snapshot.Id}/images";
        DisplayName = Snapshot.GetValue<string>("displayName");
        Name = Snapshot.GetValue<string>("name");
        Description = Snapshot.GetValue<string>("description");
        ShortDescription = Snapshot.GetValue<string>("shortDescription");
        IsVisible = Snapshot.GetValue<bool>("visible");
        LogoImage = Snapshot.TryGetValue("logo", out string logoId)
            ? ImageLink.Open(imagesPath, logoId)
            : null;
        Screenshots = Snapshot.TryGetValue("screenshots", out string[] screenshots)
            ? screenshots
                .Select(id => ImageLink.Open(imagesPath, id))
                .ToArray()
            : Array.Empty<ImageLink>();
    }

    public DocumentSnapshot Snapshot { get; }

    public string DisplayName { get; }

    public string Name { get; }

    public string Description { get; }

    public string ShortDescription { get; }

    public bool IsVisible { get; }

    public ImageLink? LogoImage { get; }

    public ImageLink[] Screenshots { get; }

    public static async ValueTask<PackageLink> OpenAsync(DocumentReference reference, CancellationToken cancellationToken = default)
    {
        DocumentSnapshot snapshot = await reference.GetSnapshotAsync(cancellationToken);
        return new PackageLink(snapshot);
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

    public IObservable<IPackage.ILink> GetObservable()
    {
        return Snapshot.Reference.ToObservable()
            .Where(snapshot => snapshot.UpdateTime != Snapshot.UpdateTime)
            .Select(snapshot => new PackageLink(snapshot));
    }

    public async ValueTask PermanentlyDeleteAsync()
    {
        DocumentReference reference = Snapshot.Reference;

        if (LogoImage is ImageLink logoImage)
        {
            await logoImage.DeleteAsync();
        }

        foreach (ImageLink item in Screenshots)
        {
            await item.DeleteAsync();
        }

        await reference.DeleteAsync();

        // Todo: どこかから'releases','resources'を表すクラスをとってきて削除するメソッドを実行する
        //reference.Collection("releases").DeleteAsync();
        //reference.Collection("resources").DeleteAsync();
    }

    public async ValueTask<IPackage.ILink> RefreshAsync(CancellationToken cancellationToken = default)
    {
        return await OpenAsync(Snapshot.Reference, cancellationToken);
    }

    public async ValueTask<IPackage.ILink> SyncronizeToAsync(IPackage value, PackageInfoFields fieldsMask, CancellationToken cancellationToken = default)
    {
        DocumentReference reference = Snapshot.Reference;
        var dict = new Dictionary<string, object>();
        if (!fieldsMask.HasFlag(PackageInfoFields.Name))
        {
            dict["name"] = value.Name;
        }
        if (!fieldsMask.HasFlag(PackageInfoFields.DisplayName))
        {
            dict["displayName"] = value.DisplayName;
        }
        if (!fieldsMask.HasFlag(PackageInfoFields.Description))
        {
            dict["description"] = value.Description;
        }
        if (!fieldsMask.HasFlag(PackageInfoFields.ShortDescription))
        {
            dict["shortDescription"] = value.ShortDescription;
        }
        if (!fieldsMask.HasFlag(PackageInfoFields.IsVisible))
        {
            dict["visible"] = value.IsVisible;
        }
        if (!fieldsMask.HasFlag(PackageInfoFields.LogoImage) && value.LogoImage is ImageLink logoImage)
        {
            dict["logo"] = logoImage.Name;
        }
        if (!fieldsMask.HasFlag(PackageInfoFields.Screenshots) && value.Screenshots.Length > 0)
        {
            dict["screenshots"] = value.Screenshots.Select(item => item.Name).ToArray();
        }

        cancellationToken.ThrowIfCancellationRequested();

        await reference.UpdateAsync(dict, cancellationToken: cancellationToken);

        return await OpenAsync(reference, cancellationToken);
    }

    public async ValueTask<IPackage.ILink> ChangeVisibility(bool visibility, CancellationToken cancellationToken = default)
    {
        DocumentReference reference = Snapshot.Reference;

        await reference.UpdateAsync("visible", visibility, cancellationToken: cancellationToken);

        return await OpenAsync(reference, cancellationToken);
    }
}
