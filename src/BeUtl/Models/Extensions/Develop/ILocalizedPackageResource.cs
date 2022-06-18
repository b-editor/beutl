using System.Globalization;
using System.Reactive.Linq;

using Google.Cloud.Firestore;

namespace BeUtl.Models.Extensions.Develop;

public record LocalizedPackageResource(
    string? DisplayName,
    string? Description,
    string? ShortDescription,
    ImageLink? LogoImage,
    ImageLink[] Screenshots,
    CultureInfo Culture)
    : ILocalizedPackageResource;

public class LocalizedPackageResourceLink : ILocalizedPackageResource.ILink
{
    public LocalizedPackageResourceLink(DocumentSnapshot snapshot)
    {
        Snapshot = snapshot;

        DocumentReference package = snapshot.Reference.Parent.Parent;
        DocumentReference user = package.Parent.Parent;
        string imagesPath = $"users/{user.Id}/packages/{package.Id}/images";
        DisplayName = Snapshot.TryGetValue("displayName", out string displayName) ? displayName : null;
        Description = Snapshot.TryGetValue("description", out string description) ? description : null;
        ShortDescription = Snapshot.TryGetValue("shortDescription", out string shortDescription) ? shortDescription : null;
        LogoImage = Snapshot.TryGetValue("logo", out string logoId)
            ? ImageLink.Open(imagesPath, logoId)
            : null;
        Screenshots = Snapshot.TryGetValue("screenshots", out string[] screenshots)
            ? screenshots
                .Select(id => ImageLink.Open(imagesPath, id))
                .ToArray()
            : Array.Empty<ImageLink>();
        Culture = CultureInfo.GetCultureInfo(Snapshot.GetValue<string>("culture"));
    }

    public DocumentSnapshot Snapshot { get; }

    public string? DisplayName { get; }

    public string? Description { get; }

    public string? ShortDescription { get; }

    public ImageLink? LogoImage { get; }

    public ImageLink[] Screenshots { get; }

    public CultureInfo Culture { get; }

    public static async ValueTask<LocalizedPackageResourceLink> OpenAsync(DocumentReference reference, CancellationToken cancellationToken = default)
    {
        DocumentSnapshot snapshot = await reference.GetSnapshotAsync(cancellationToken);
        return new LocalizedPackageResourceLink(snapshot);
    }

    public IObservable<ILocalizedPackageResource.ILink> GetObservable()
    {
        return Snapshot.Reference.ToObservable()
            .Select(snapshot => new LocalizedPackageResourceLink(snapshot));
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
    }

    public async ValueTask<ILocalizedPackageResource.ILink> RefreshAsync(CancellationToken cancellationToken = default)
    {
        return await OpenAsync(Snapshot.Reference, cancellationToken);
    }

    public async ValueTask<ILocalizedPackageResource.ILink> SyncronizeToAsync(ILocalizedPackageResource value, PackageResourceFields fieldsMask, CancellationToken cancellationToken = default)
    {
        DocumentReference reference = Snapshot.Reference;
#pragma warning disable IDE0055
        var dict = new Dictionary<string, object?>
        {
            ["displayName"]      = (fieldsMask.HasFlag(PackageResourceFields.DisplayName)      ? this : value).DisplayName,
            ["description"]      = (fieldsMask.HasFlag(PackageResourceFields.Description)      ? this : value).Description,
            ["shortDescription"] = (fieldsMask.HasFlag(PackageResourceFields.ShortDescription) ? this : value).ShortDescription,
            ["logo"]             = (fieldsMask.HasFlag(PackageResourceFields.LogoImage)        ? this : value).LogoImage?.Name,
            ["screenshots"]      = (fieldsMask.HasFlag(PackageResourceFields.Screenshots)      ? this : value).Screenshots.Select(item => item.Name).ToArray(),
            ["culture"]          = (fieldsMask.HasFlag(PackageResourceFields.Culture)          ? this : value).Culture.Name
        };
#pragma warning restore IDE0055

        cancellationToken.ThrowIfCancellationRequested();

        foreach (string item in dict.Keys)
        {
            if (dict[item] == null)
                dict.Remove(item);
        }

        await reference.SetAsync(dict, SetOptions.Overwrite, cancellationToken: cancellationToken);

        return await OpenAsync(reference, cancellationToken);
    }
}

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
