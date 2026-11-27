using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beutl.IO;

namespace Beutl.Agents;

public sealed partial class AgentThreadStore
{
    internal string ProjectDirectory(string projectPath)
    {
        string canonicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
        if (OperatingSystem.IsWindows())
            canonicalPath = canonicalPath.ToUpperInvariant();

        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPath)));
        return Path.Combine(_root, hash);
    }

    private static string TimelinePath(string directory, Guid id) => Path.Combine(directory, $"{id:N}.jsonl");

    internal static async Task<FileStream> LockAsync(string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, ".lock");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(30, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal static async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        using var output = new StagedOutputFile(path);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using (var stream = new FileStream(output.TemporaryPath, options))
            await JsonSerializer.SerializeAsync(stream, value, AgentJson.Options, cancellationToken).ConfigureAwait(false);
        output.Commit(cancellationToken);
    }

    private static async Task<List<AgentThreadMetadata>> ReadIndexAsync(string directory, CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, "index.json");
        if (!File.Exists(path))
            return [];
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<AgentThreadMetadata>>(stream, AgentJson.Options, cancellationToken)
            .ConfigureAwait(false) ?? throw new JsonException("Expected an agent thread index.");
    }

    private static Task WriteIndexAsync(
        string directory, List<AgentThreadMetadata> threads, CancellationToken cancellationToken)
        => WriteJsonAtomicAsync(Path.Combine(directory, "index.json"), threads, cancellationToken);

    private static async Task WriteTimelineAsync(
        string path, IEnumerable<AgentTimelineEvent> entries, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await JsonSerializer.SerializeAsync<AgentTimelineEvent>(stream, entry, AgentJson.Options, cancellationToken)
                .ConfigureAwait(false);
            stream.WriteByte((byte)'\n');
        }
    }

    private static async Task AppendTimelineFileAsync(string directory, AgentTimelineEvent entry, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(TimelinePath(directory, entry.ThreadId), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        RepairIncompleteLine(stream);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(entry, AgentJson.Options);
        cancellationToken.ThrowIfCancellationRequested();
        long start = stream.Position;
        try
        {
            await stream.WriteAsync(json).ConfigureAwait(false);
            stream.WriteByte((byte)'\n');
            await stream.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            stream.SetLength(start);
            throw;
        }
    }

    private static async Task<List<AgentTimelineEvent>> ReadTimelineFileAsync(string path, CancellationToken cancellationToken)
    {
        var entries = new List<AgentTimelineEvent>();
        await foreach (var line in ReadTimelineLinesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            entries.Add(JsonSerializer.Deserialize<AgentTimelineEvent>(line, AgentJson.Options)
                ?? throw new JsonException("Expected a timeline event."));
        }
        return entries;
    }

    private static async IAsyncEnumerable<string> ReadTimelineLinesAsync(
        string path, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            yield break;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        bool complete = stream.Length == 0 || stream.ReadByteAtEnd() == '\n';
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        while (line != null)
        {
            string? next = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (!complete && next == null)
                yield break;
            yield return line;
            line = next;
        }
    }

    private static void RepairIncompleteLine(FileStream stream)
    {
        if (stream.Length == 0 || stream.ReadByteAtEnd() == '\n')
            return;

        Span<byte> buffer = stackalloc byte[4096];
        long end = stream.Length;
        while (end > 0)
        {
            int count = (int)Math.Min(end, buffer.Length);
            long offset = end - count;
            stream.Position = offset;
            stream.ReadExactly(buffer[..count]);
            int lastNewline = buffer[..count].LastIndexOf((byte)'\n');
            if (lastNewline >= 0)
            {
                stream.Position = offset + lastNewline + 1;
                stream.SetLength(stream.Position);
                return;
            }
            end = offset;
        }
        stream.SetLength(0);
        stream.Position = 0;
    }
}

file static class TimelineStreamExtensions
{
    internal static int ReadByteAtEnd(this FileStream stream)
    {
        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte();
    }
}
