internal static class ReleaseLegalFiles
{
    private static readonly (string SourcePath, string OutputName)[] s_files =
    [
        ("LICENSE", "LICENSE"),
        ("LICENSE.GPL", "LICENSE.GPL"),
        ("THIRD_PARTY_NOTICES.md", "THIRD_PARTY_NOTICES.md"),
        (Path.Combine("src", "Beutl.FFmpegWorker", "LICENSE"), "LICENSE.FFmpegWorker"),
    ];

    internal static void CopyTo(string repositoryRoot, string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        Directory.CreateDirectory(destinationDirectory);
        foreach ((string sourcePath, string outputName) in s_files)
        {
            File.Copy(
                Path.Combine(repositoryRoot, sourcePath),
                Path.Combine(destinationDirectory, outputName),
                overwrite: true);
        }
    }
}
