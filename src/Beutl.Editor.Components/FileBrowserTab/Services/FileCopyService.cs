namespace Beutl.Editor.Components.FileBrowserTab.Services;

internal static class FileCopyService
{
    // 既に存在するファイルはスキップされる。
    // キャンセルは次のファイルかフォルダーの前に反映される。
    public static void CopyDirectoryRecursive(
        string sourceDir,
        string destDir,
        Action<string, string> copyFile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullSource = Path.GetFullPath(sourceDir) + Path.DirectorySeparatorChar;
        string fullDest = Path.GetFullPath(destDir) + Path.DirectorySeparatorChar;

        if (fullDest.StartsWith(fullSource, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Cannot copy a directory into itself or a descendant directory.");
        }

        Directory.CreateDirectory(destDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            if (!File.Exists(destFile))
            {
                copyFile(file, destFile);
            }
        }

        foreach (string dir in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectoryRecursive(dir, destSubDir, copyFile, cancellationToken);
        }
    }
}
