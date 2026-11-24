namespace Beutl.Services;

internal static class UpdateFolders
{
    private static readonly object s_claimGate = new();
    private static FileStream? s_updateFolderClaim;

    // Where an update is unpacked before the update script copies it over the application.
    internal static string UpdateStagingRoot => GetPath("update");

    // Downloads go to their own folder, so a server-chosen name cannot overwrite the update
    // scripts in tmp, which this application later executes.
    internal static string DownloadDirectory => GetPath("download");

    // A zip update unpacks into a folder named like the one the application runs from.
    internal static string ZipStagingDirectory
        => GetZipStagingDirectory(AppContext.BaseDirectory, UpdateStagingRoot);

    internal static string GetZipStagingDirectory(string appDirectory, string stagingRoot)
        => Path.Combine(stagingRoot, new DirectoryInfo(appDirectory).Name);

    internal static string GetScriptPath(string fileName) => GetPath(fileName);

    private static string GetPath(string name)
        => Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", name);

    // Every running Beutl unpacks its update into the same folders, and unpacking empties them first. So only the Beutl
    // that claims them unpacks one, or starts one unasked, and the claim lasts until the process exits. The lock file
    // stays, since unlinking it could split ownership.
    internal static bool Claim()
    {
        lock (s_claimGate)
        {
            s_updateFolderClaim ??= TryClaim(GetPath("update.lock"));
            return s_updateFolderClaim != null;
        }
    }

    // The lock on path, or null while another process holds it.
    internal static FileStream? TryClaim(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Creates an empty staging folder in the home directory's tmp folder, which belongs to Beutl.
    internal static void PrepareStagingDirectory(string destination)
    {
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, true);
        }

        Directory.CreateDirectory(destination);
    }
}
