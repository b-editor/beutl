namespace Beutl.Editor.Components.FileBrowserTab.Services;

internal static class FileCopyService
{
    // 既に存在するファイルはスキップし、コピー元とコピー先のフォルダーへのリンクはたどらずにスキップして、その数を返す。
    // キャンセルは次のファイルかフォルダーの前に反映される。
    public static int CopyDirectoryRecursive(
        string sourceDir,
        string destDir,
        string allowedRoot,
        Action<string, string> copyFile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // ルート配下のリンクだけを拒否し、解決後もコピー先がルート内であることを確認する。
        string relativeDestination = Path.GetRelativePath(allowedRoot, destDir);
        if (Path.IsPathRooted(relativeDestination)
            || relativeDestination == ".."
            || relativeDestination.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !FilePathComparison.IsSameOrDescendant(allowedRoot, destDir))
        {
            return 1;
        }

        string destinationPath = allowedRoot;
        foreach (string component in relativeDestination.Split(Path.DirectorySeparatorChar))
        {
            if (component == ".")
                continue;

            destinationPath = Path.Combine(destinationPath, component);
            var destination = new DirectoryInfo(destinationPath);
            if (destination.LinkTarget is not null)
            {
                return 1;
            }

            if (!destination.Exists)
                break;
        }

        if (FilePathComparison.IsSameOrDescendant(sourceDir, destDir))
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

        int skippedLinks = 0;
        foreach (string dir in Directory.GetDirectories(sourceDir))
        {
            if (new DirectoryInfo(dir).LinkTarget is not null)
            {
                skippedLinks++;
                continue;
            }

            string destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            skippedLinks += CopyDirectoryRecursive(dir, destSubDir, allowedRoot, copyFile, cancellationToken);
        }

        return skippedLinks;
    }
}
