namespace Beutl.Editor;

internal enum ProjectAssetAction { Collect, Quarantine, Restore, Delete }

internal enum ProjectAssetIssue
{
    Missing,
    UnsafePath,
    SharedReference,
    IncompleteReferences,
    EditHistory,
    InUse,
    DestinationExists,
    Unavailable,
}

internal sealed record ProjectAssetItem(ProjectAssetFile[] Files, ProjectAssetIssue? Issue = null)
{
    public string Path => Files[0].Path;
    public string Destination => Files[0].Destination;
    public long Size => Files.Sum(file => file.Stamp?.Length ?? 0);

    internal bool CanApply(ProjectAssetAction action)
        => Issue == null || action == ProjectAssetAction.Delete && Issue == ProjectAssetIssue.DestinationExists;
}

internal readonly record struct ProjectAssetFile(string Path, string Destination, ProjectAssetStamp? Stamp);

internal readonly record struct ProjectAssetStamp(long Length, DateTime LastWriteTimeUtc, DateTime CreationTimeUtc)
{
    internal static ProjectAssetStamp? Read(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? new(file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc) : null;
    }
}

internal sealed record ProjectAssetPreview(Guid Batch, ProjectAssetItem[] External, ProjectAssetItem[] Unused,
    ProjectAssetItem[] Quarantined, ProjectAssetIssue? CleanupIssue)
{
    internal ProjectAssetItem[] ForAction(ProjectAssetAction action) => action switch
    {
        ProjectAssetAction.Collect => External,
        ProjectAssetAction.Quarantine => Unused,
        _ => Quarantined,
    };
}
