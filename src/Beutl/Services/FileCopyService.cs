namespace Beutl.Services;

// ファイルおよびディレクトリのコピー操作を提供するユーティリティ。
internal static class FileCopyService
{
    // ソースディレクトリの内容を再帰的に宛先ディレクトリにコピーする。
    // 既に存在するファイルはスキップされる。
    public static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            if (!File.Exists(destFile))
            {
                File.Copy(file, destFile);
            }
        }

        foreach (string dir in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectoryRecursive(dir, destSubDir);
        }
    }
}
