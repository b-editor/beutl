namespace Beutl.AgentToolkit.Installation;

// Replaces an agent's MCP configuration file through a flushed temporary file, so a failed or cancelled write
// never leaves it truncated.
internal static class McpConfigFile
{
    // writeContents is a test hook that replaces the write of the new contents.
    public static async Task ReplaceAsync(
        string path,
        string text,
        Func<Stream, string, CancellationToken, Task>? writeContents,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        // The replacement keeps the permissions of the file it replaces.
        UnixFileMode? mode = null;
        if (!OperatingSystem.IsWindows() && File.Exists(path))
        {
            mode = File.GetUnixFileMode(path);
        }

        string temporaryPath = Path.Combine(directory, $".beutl-mcp-{Guid.NewGuid():N}.tmp");
        var streamOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
        };
        if (!OperatingSystem.IsWindows() && mode is { } createMode)
        {
            streamOptions.UnixCreateMode = createMode;
        }

        try
        {
            await using (var temporary = new FileStream(temporaryPath, streamOptions))
            {
                // The umask can narrow the mode the file is created with.
                if (!OperatingSystem.IsWindows() && mode is { } existingMode)
                {
                    File.SetUnixFileMode(temporary.SafeFileHandle, existingMode);
                }

                await (writeContents ?? WriteContentsAsync)(temporary, text, cancellationToken).ConfigureAwait(false);
                temporary.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static async Task WriteContentsAsync(Stream stream, string text, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(stream, leaveOpen: true);
        await writer.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
