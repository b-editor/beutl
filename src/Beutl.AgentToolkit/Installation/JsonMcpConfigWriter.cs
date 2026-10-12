using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beutl.AgentToolkit.Installation;

// Merges the Beutl server into another product's JSON configuration file. The file is rewritten only
// when the merge changes it, through a flushed temporary file that then replaces it, so a failed write
// never leaves it truncated. A file with comments is left as it is, because writing it back would drop
// them; the caller shows the entry to add by hand instead.
internal static class JsonMcpConfigWriter
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    // Returns null when the file now holds the entry, or the edit to make by hand. writeContents is a
    // test hook that replaces the write of the new contents.
    public static async Task<ManualMcpEdit?> WriteAsync(
        string path,
        AgentToolkitInstallOptions options,
        CancellationToken cancellationToken,
        Func<Stream, string, CancellationToken, Task>? writeContents = null)
    {
        // Replace the resolved target, preserving an existing config symlink.
        string target = PathBoundary.ResolveDeepestExistingTarget(Path.GetFullPath(path));
        string text = File.Exists(target)
            ? await File.ReadAllTextAsync(target, cancellationToken).ConfigureAwait(false)
            : "";
        JsonObject current = Parse(text, target);
        var updated = (JsonObject)current.DeepClone();
        JsonObject servers = GetOrCreateObject(updated, options.McpServersPropertyName);
        servers[options.McpServerName] = CreateServer(options);
        string[] obsolete = AgentToolkitInstaller.ObsoleteServerNames().Where(servers.Remove).ToArray();

        string json = updated.ToJsonString(s_jsonOptions);
        if (JsonNode.DeepEquals(current, JsonNode.Parse(json)))
        {
            return null;
        }

        if (HasComments(text))
        {
            var entry = new JsonObject();
            GetOrCreateObject(entry, options.McpServersPropertyName)[options.McpServerName] = CreateServer(options);
            return new ManualMcpEdit(path, entry.ToJsonString(s_jsonOptions), obsolete);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await ReplaceAsync(target, json + Environment.NewLine, writeContents, cancellationToken).ConfigureAwait(false);
        return null;
    }

    private static JsonObject Parse(string text, string path)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        JsonNode? node = JsonNode.Parse(
            text,
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

        return node as JsonObject
               ?? throw new InvalidDataException($"MCP config must be a JSON object: {path}");
    }

    private static bool HasComments(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text), new JsonReaderOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Allow,
        });
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.Comment)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task ReplaceAsync(
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

    // A stdio launcher: the server finds and authenticates the running editors itself, so neither a
    // URL nor the bearer token is written into the agent config.
    private static JsonObject CreateServer(AgentToolkitInstallOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.McpCommand))
        {
            throw new InvalidOperationException("MCP installation requires a command.");
        }

        var args = new JsonArray();
        foreach (string arg in options.McpArguments)
        {
            args.Add(arg);
        }

        var server = new JsonObject();
        if (options.McpTypeValue is not null)
        {
            server["type"] = options.McpTypeValue;
        }

        server["command"] = options.McpCommand;
        server["args"] = args;

        var env = new JsonObject();
        foreach (KeyValuePair<string, string> pair in options.McpEnvironment)
        {
            env[pair.Key] = pair.Value;
        }

        if (env.Count > 0)
        {
            server["env"] = env;
        }

        return server;
    }

    // A dotted name (e.g. Amp's "amp.mcpServers") addresses a nested object: amp:{ mcpServers:{} }.
    // Writing it as one flat "amp.mcpServers" key would put the server where the agent never reads it.
    private static JsonObject GetOrCreateObject(JsonObject parent, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException("Property name cannot be empty.", nameof(propertyName));
        }

        JsonObject current = parent;
        foreach (string segment in propertyName.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current[segment] is null)
            {
                var obj = new JsonObject();
                current[segment] = obj;
                current = obj;
            }
            else
            {
                current = current[segment] as JsonObject
                          ?? throw new InvalidDataException($"MCP config property must be an object: {segment}");
            }
        }

        return current;
    }
}
