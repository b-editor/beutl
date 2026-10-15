using Tomlyn;
using Tomlyn.Model;

namespace Beutl.AgentToolkit.Installation;

internal static class CodexMcpConfigWriter
{
    // writeContents is a test hook that replaces the write of the new contents.
    public static async Task WriteAsync(
        string path,
        AgentToolkitInstallOptions options,
        CancellationToken cancellationToken,
        Func<Stream, string, CancellationToken, Task>? writeContents = null)
    {
        // Replace the resolved target, preserving an existing config symlink.
        path = FilePathComparison.ResolveCanonicalPath(Path.GetFullPath(path));
        string text = File.Exists(path)
            ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
            : "";
        // Parse before writing so a malformed user config is never overwritten.
        TomlTable root = TomlSerializer.Deserialize<TomlTable>(text)
                         ?? throw new InvalidDataException($"Invalid Codex MCP config: {path}");
        if (root.TryGetValue(options.McpServersPropertyName, out object? existing)
            && existing is not TomlTable)
        {
            throw new InvalidDataException("Codex MCP servers must be a TOML table.");
        }

        TomlTable servers = CreateServers(options);
        string updated = CodexMcpConfigEditor.Update(
            text, root, options.McpServersPropertyName, servers, AgentToolkitInstaller.ObsoleteServerNames());
        cancellationToken.ThrowIfCancellationRequested();
        if (updated == text)
            return;

        await McpConfigFile.ReplaceAsync(path, updated, writeContents, cancellationToken).ConfigureAwait(false);
    }

    // A stdio launcher; the server authenticates to the running editors itself, so no URL or
    // token is stored in the Codex config.
    private static TomlTable CreateServers(AgentToolkitInstallOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.McpCommand))
        {
            throw new InvalidOperationException("MCP installation requires a command.");
        }

        var arguments = new TomlArray();
        foreach (string argument in options.McpArguments)
        {
            arguments.Add(argument);
        }

        var server = new TomlTable { ["command"] = options.McpCommand, ["args"] = arguments };
        TomlTable environment = ToTable(options.McpEnvironment);
        if (environment.Count > 0)
        {
            server["env"] = environment;
        }

        return new TomlTable { [AgentToolkitInstallOptions.DefaultServerName] = server };
    }

    private static TomlTable ToTable(IReadOnlyDictionary<string, string> values)
    {
        var table = new TomlTable();
        foreach (KeyValuePair<string, string> pair in values)
        {
            table[pair.Key] = pair.Value;
        }

        return table;
    }
}
