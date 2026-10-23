namespace Beutl.Editor.Components.FileBrowserTab.Services;

internal static class FileCopyService
{
    // 既に存在するファイルはスキップし、コピー元とコピー先のフォルダーへのリンクはたどらずにスキップして、その数を返す。
    // キャンセルは次のファイルかフォルダーの前に反映される。
    public static int CopyDirectoryRecursive(
        string sourceDir,
        string destDir,
        Action<string, string> copyFile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // リンクになったコピー先に書くと、ドロップ先の外に書き込んでしまう
        if (new DirectoryInfo(destDir).LinkTarget is not null)
        {
            return 1;
        }

        // リンクを解決して比べるので、コピー元の中を指すリンクの下へのコピーも防げる
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
            skippedLinks += CopyDirectoryRecursive(dir, destSubDir, copyFile, cancellationToken);
        }

        return skippedLinks;
    }
}
