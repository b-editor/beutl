using Tomlyn;
using Tomlyn.Model;

namespace Beutl.AgentToolkit.Installation;

internal static class CodexMcpConfigWriter
{
    public static async Task WriteAsync(
        string path,
        AgentToolkitInstallOptions options,
        CancellationToken cancellationToken)
    {
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

        var servers = new TomlTable();
        if (options.InstallStdioMcp)
        {
            if (string.IsNullOrWhiteSpace(options.StdioMcpCommand))
            {
                throw new InvalidOperationException("Stdio MCP installation requires a command.");
            }

            var arguments = new TomlArray();
            foreach (string argument in options.StdioMcpArguments)
            {
                arguments.Add(argument);
            }

            var server = new TomlTable { ["command"] = options.StdioMcpCommand, ["args"] = arguments };
            TomlTable environment = ToTable(options.StdioMcpEnvironment);
            if (!string.IsNullOrWhiteSpace(options.WorkspaceRoot)
                && !environment.ContainsKey("BEUTL_WORKSPACE"))
            {
                environment["BEUTL_WORKSPACE"] = Path.GetFullPath(options.WorkspaceRoot);
            }

            if (environment.Count > 0)
            {
                server["env"] = environment;
            }

            servers[options.StdioMcpServerName] = server;
        }

        if (options.InstallLiveMcp)
        {
            if (options.LiveMcpUri is null)
            {
                throw new InvalidOperationException("Live MCP installation requires a live MCP URI.");
            }

            var server = new TomlTable { ["url"] = options.LiveMcpUri.ToString() };
            if (options.LiveMcpHeaders.Count > 0)
            {
                server["http_headers"] = ToTable(options.LiveMcpHeaders);
            }

            servers[options.LiveMcpServerName] = server;
        }

        string updated = CodexMcpConfigEditor.Update(text, root, options.McpServersPropertyName, servers);
        if (updated == text)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, updated, cancellationToken).ConfigureAwait(false);
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
