using Beutl.AgentToolkit.Installation;
using Tomlyn;
using Tomlyn.Model;

namespace Beutl.AgentToolkit.Tests.Installation;

public sealed class CodexMcpConfigWriterTests
{
    private const string ServerCommand = "beutl-mcp";
    private const string ProfileHome = "/home/user/.beutl";
    private const string OtherHome = "/home/user/other-profile";

    private string _root = null!;

    private string ConfigPath => Path.Combine(_root, ".codex", "config.toml");

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "beutl-codex-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    // One stdio entry for live and headless editing; the config never carries a URL or the token.
    private AgentToolkitInstallOptions Options => new()
    {
        AgentRoot = _root,
        InstallSkills = false,
        InstallSubagents = false,
        McpConfigFileName = Path.Combine(".codex", "config.toml"),
        McpConfigFormat = McpConfigFormat.CodexToml,
        McpServersPropertyName = "mcp_servers",
        McpCommand = ServerCommand,
        McpEnvironment = new Dictionary<string, string> { ["BEUTL_HOME"] = ProfileHome },
    };

    private static AgentToolkitInstallOptions WithHome(AgentToolkitInstallOptions options, string home)
        => options with { McpEnvironment = new Dictionary<string, string> { ["BEUTL_HOME"] = home } };

    private static TomlTable Server(TomlTable servers, string name = "beutl-agent") => (TomlTable)servers[name];

    private static string Home(TomlTable server) => (string)((TomlTable)server["env"])["BEUTL_HOME"];

    [TestCase("mcp_servers", "team.stdio")]
    [TestCase("mcp.servers name", "server \"quoted\"")]
    public async Task Non_bare_key_segments_are_quoted_and_reinstall_matches_the_same_server(
        string serversProperty, string serverName)
    {
        AgentToolkitInstallOptions options = Options with
        {
            McpServersPropertyName = serversProperty,
            McpServerName = serverName,
        };
        await AgentToolkitInstaller.InstallAsync(options, []);
        await AgentToolkitInstaller.InstallAsync(WithHome(options, OtherHome), []);

        TomlTable root = TomlSerializer.Deserialize<TomlTable>(await File.ReadAllTextAsync(ConfigPath))!;
        var servers = (TomlTable)root[serversProperty];
        Assert.Multiple(() =>
        {
            Assert.That(root.Keys, Is.EqualTo(new[] { serversProperty }));
            Assert.That(servers.Keys, Is.EqualTo(new[] { serverName }));
            Assert.That(Server(servers, serverName)["command"], Is.EqualTo(ServerCommand));
            Assert.That(Home(Server(servers, serverName)), Is.EqualTo(OtherHome));
        });
    }

    [Test]
    public async Task New_codex_config_is_private_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file modes are not available on Windows.");
            return;
        }

        await AgentToolkitInstaller.InstallAsync(Options, []);

        Assert.That(File.GetUnixFileMode(ConfigPath), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Reinstall_restricts_existing_config_permissions_even_when_content_is_unchanged(bool unchanged)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file modes are not available on Windows.");
            return;
        }

        await AgentToolkitInstaller.InstallAsync(Options, []);
        File.SetUnixFileMode(ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        AgentToolkitInstallOptions options = unchanged ? Options : WithHome(Options, OtherHome);

        await AgentToolkitInstaller.InstallAsync(options, []);

        Assert.That(File.GetUnixFileMode(ConfigPath), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        var servers = (TomlTable)TomlSerializer.Deserialize<TomlTable>(await File.ReadAllTextAsync(ConfigPath))!["mcp_servers"];
        Assert.That(Home(Server(servers)), Is.EqualTo(options.McpEnvironment["BEUTL_HOME"]));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Failed_or_cancelled_writes_preserve_the_original_and_remove_temporary_files(bool exists, bool cancel)
    {
        const string original = "# User configuration\nmodel = 'custom'\n\n[mcp_servers.other]\ncommand = 'keep this'\n";
        if (exists)
            await File.WriteAllTextAsync(ConfigPath, original);
        using var cancellation = new CancellationTokenSource();

        Task Write() => CodexMcpConfigWriter.WriteAsync(ConfigPath, Options, cancellation.Token,
            async (stream, _, token) =>
            {
                await stream.WriteAsync("partial"u8.ToArray(), CancellationToken.None);
                if (cancel)
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                }

                throw new IOException("Simulated write failure.");
            });

        if (cancel)
            await Assert.ThrowsAsync<OperationCanceledException>(Write);
        else
            await Assert.ThrowsAsync<IOException>(Write);
        Assert.That(File.Exists(ConfigPath), Is.EqualTo(exists));
        if (exists)
            Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(original));
        Assert.That(Directory.EnumerateFiles(Path.GetDirectoryName(ConfigPath)!, "*.tmp"), Is.Empty);
    }

    [Test]
    public async Task Installation_preserves_a_config_symlink_and_updates_its_target()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Creating symlinks requires additional Windows privileges.");
            return;
        }

        string target = Path.Combine(_root, "managed-config.toml");
        await File.WriteAllTextAsync(target, "model = 'custom'\n");
        File.CreateSymbolicLink(ConfigPath, target);

        await AgentToolkitInstaller.InstallAsync(Options, []);

        Assert.That(new FileInfo(ConfigPath).LinkTarget, Is.EqualTo(target));
        TomlTable root = TomlSerializer.Deserialize<TomlTable>(await File.ReadAllTextAsync(target))!;
        Assert.That(root["model"], Is.EqualTo("custom"));
        Assert.That(((TomlTable)root["mcp_servers"]).ContainsKey("beutl-agent"), Is.True);
        Assert.That(File.GetUnixFileMode(target), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
    }

    [Test]
    public async Task Fresh_install_writes_launcher_and_environment_tables()
    {
        await AgentToolkitInstaller.InstallAsync(Options, []);

        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(
            "[mcp_servers.beutl-agent]\ncommand = \"beutl-mcp\"\nargs = []\n\n"
            + "[mcp_servers.beutl-agent.env]\nBEUTL_HOME = \"/home/user/.beutl\"\n"));
    }

    [Test]
    public void Install_requires_the_server_command()
    {
        Assert.ThrowsAsync<InvalidOperationException>(() => AgentToolkitInstaller.InstallAsync(
            Options with { McpCommand = null }, []));
        Assert.That(File.Exists(ConfigPath), Is.False);
    }

    [TestCase("\n")]
    [TestCase("\r\n")]
    public async Task Previously_generated_dotted_keys_are_moved_into_tables(string newline)
    {
        string original = string.Join(newline,
        [
            "# User settings",
            "model = 'custom-model'",
            "", "",
            "mcp_servers.beutl-agent.command = \"old-command\" # Keep this comment",
            "mcp_servers.beutl-agent.env.BEUTL_WORKSPACE = \"/old/workspace\"",
            "",
            "[mcp_servers.other]",
            "command   = 'keep this'",
            "", "",
        ]);
        await File.WriteAllTextAsync(ConfigPath, original);

        await AgentToolkitInstaller.InstallAsync(Options, []);

        string updated = await File.ReadAllTextAsync(ConfigPath);
        Assert.Multiple(() =>
        {
            Assert.That(updated, Does.Contain($"[mcp_servers.beutl-agent]{newline}command = \"beutl-mcp\"{newline}args = []{newline}"));
            Assert.That(updated, Does.Contain($"[mcp_servers.beutl-agent.env]{newline}BEUTL_HOME = \"/home/user/.beutl\"{newline}"));
            Assert.That(updated, Does.Not.Contain("mcp_servers.beutl-agent.command ="));
            Assert.That(updated, Does.Not.Contain("BEUTL_WORKSPACE"));
            Assert.That(updated, Does.Contain($"model = 'custom-model'{newline}{newline}{newline}"));
            Assert.That(updated, Does.Contain("# Keep this comment"));
            Assert.That(updated, Does.Contain($"[mcp_servers.other]{newline}command   = 'keep this'{newline}{newline}"));
        });

        await AgentToolkitInstaller.InstallAsync(Options, []);

        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(updated));
    }

    [Test]
    public async Task Adding_an_environment_to_an_existing_launcher_creates_an_env_table()
    {
        const string original = "[mcp_servers.beutl-agent]\ncommand = \"beutl-mcp\"\nargs = []\n\n";
        await File.WriteAllTextAsync(ConfigPath, original);

        await AgentToolkitInstaller.InstallAsync(Options, []);

        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(original
            + "[mcp_servers.beutl-agent.env]\nBEUTL_HOME = \"/home/user/.beutl\"\n"));
    }

    [TestCase("\n")]
    [TestCase("\r\n")]
    public async Task New_server_tables_follow_the_last_existing_mcp_table_before_other_settings(string newline)
    {
        string prefix = string.Join(newline,
        [
            "model = 'custom'",
            "",
            "[mcp_servers.first]",
            "command = 'first'",
            "",
            "[features]",
            "enabled = true",
            "",
            "[\"mcp_servers\".\"last.server\"]",
            "url = 'https://example.com/mcp'",
            "",
            "[mcp_servers.\"last.server\".http_headers]",
            "Existing = 'keep this' # Existing header",
            "",
        ]);
        string suffix = string.Join(newline,
        [
            "", "",
            "# Project settings stay with their table",
            "[projects.\"/work/example\"]",
            "trust_level = 'trusted'",
            "",
            "[other]",
            "description = '[mcp_servers.fake]'",
            "",
        ]);
        await File.WriteAllTextAsync(ConfigPath, prefix + suffix);

        await AgentToolkitInstaller.InstallAsync(Options, []);

        string inserted = $"{newline}[mcp_servers.beutl-agent]{newline}command = \"beutl-mcp\"{newline}args = []{newline}"
                          + $"{newline}[mcp_servers.beutl-agent.env]{newline}BEUTL_HOME = \"/home/user/.beutl\"{newline}";
        string expected = prefix + inserted + suffix;
        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(expected));

        await AgentToolkitInstaller.InstallAsync(Options, []);

        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(expected));
    }

    [Test]
    public async Task New_env_table_follows_an_updated_server_before_the_next_settings_table()
    {
        const string original = "[mcp_servers.beutl-agent]\ncommand = 'old'\n\n# Project settings\n[projects.example]\ntrust_level = 'trusted'\n";
        await File.WriteAllTextAsync(ConfigPath, original);

        await AgentToolkitInstaller.InstallAsync(Options, []);

        string updated = await File.ReadAllTextAsync(ConfigPath);
        int server = updated.IndexOf("[mcp_servers.beutl-agent]", StringComparison.Ordinal);
        int command = updated.IndexOf("command = ", StringComparison.Ordinal);
        int environment = updated.IndexOf("[mcp_servers.beutl-agent.env]", StringComparison.Ordinal);
        int project = updated.IndexOf("# Project settings", StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(command, Is.GreaterThan(server).And.LessThan(environment));
            Assert.That(environment, Is.GreaterThan(server).And.LessThan(project));
            Assert.That(updated, Does.EndWith("\n\n# Project settings\n[projects.example]\ntrust_level = 'trusted'\n"));
            var servers = (TomlTable)TomlSerializer.Deserialize<TomlTable>(updated)!["mcp_servers"];
            Assert.That(Server(servers)["command"], Is.EqualTo(ServerCommand));
            Assert.That(Home(Server(servers)), Is.EqualTo(ProfileHome));
        });

        await AgentToolkitInstaller.InstallAsync(Options, []);

        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(updated));
    }

    [TestCase("# Codex settings\n\n")]
    [TestCase("# Codex settings")]
    [TestCase("[features]\nenabled = true\n\n# Keep this footer\n")]
    public async Task Without_an_mcp_table_new_servers_follow_the_existing_file_including_comments(string original)
    {
        await File.WriteAllTextAsync(ConfigPath, original);

        await AgentToolkitInstaller.InstallAsync(Options, []);

        string updated = await File.ReadAllTextAsync(ConfigPath);
        Assert.That(updated, Does.StartWith(original));
        Assert.That(updated.IndexOf("[mcp_servers.beutl-agent]", StringComparison.Ordinal), Is.GreaterThanOrEqualTo(original.Length));
        var servers = (TomlTable)TomlSerializer.Deserialize<TomlTable>(updated)!["mcp_servers"];
        Assert.That(Server(servers)["command"], Is.EqualTo(ServerCommand));
    }

    [TestCase("\n")]
    [TestCase("\r\n")]
    public async Task Reinstall_changes_only_values_and_preserves_blank_lines_and_formatting(string newline)
    {
        string original = string.Join(newline,
        [
            "# User settings 日本語 🎬",
            "model   = 'custom-model'   # Keep spacing",
            "", "",
            "[mcp_servers.other]",
            "args = [ 'one',  'two' ]",
            "command = 'other'",
            "",
            "# Beutl server",
            "[mcp_servers.\"beutl-agent\"]  # Keep header",
            "command  = \"old-command\"   # Keep comment",
            "args = []",
            "", "",
            "[mcp_servers.'beutl-agent'.env]",
            "BEUTL_HOME = \"/old/home\"",
            "", "",
            "[projects.\"/work/a.b\"]",
            "trust_level  = 'trusted'",
            "", "",
        ]);
        await File.WriteAllTextAsync(ConfigPath, original);

        await AgentToolkitInstaller.InstallAsync(Options, []);

        string expected = original
            .Replace("\"old-command\"", "\"beutl-mcp\"")
            .Replace("\"/old/home\"", "\"/home/user/.beutl\"");
        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(expected));

        await AgentToolkitInstaller.InstallAsync(Options, []);

        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(expected), "Reinstalling unchanged settings must preserve the entire file.");
    }

    [Test]
    public async Task Adding_a_server_preserves_all_existing_text()
    {
        const string original = "# Keep this header\n\nmodel = 'custom'\n\n\n[mcp_servers.other]\ncommand   = 'other'\n\n# Footer\n";
        await File.WriteAllTextAsync(ConfigPath, original);

        await AgentToolkitInstaller.InstallAsync(Options, []);

        string updated = await File.ReadAllTextAsync(ConfigPath);
        Assert.That(updated, Does.Contain("# Keep this header\n\nmodel = 'custom'\n"));
        Assert.That(updated, Does.Contain("\n\n[mcp_servers.other]\ncommand   = 'other'\n"));
        Assert.That(updated, Does.EndWith("\n\n# Footer\n"));
    }

    [TestCase("mcp_servers = { other = { command = 'other' },  beutl-agent = { command = \"old-command\", args = [], env = { BEUTL_HOME = \"/old/home\" } } }\n\n")]
    public async Task Inline_server_updates_preserve_original_spacing(string original)
    {
        await File.WriteAllTextAsync(ConfigPath, original);

        await AgentToolkitInstaller.InstallAsync(Options, []);

        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(original
            .Replace("\"old-command\"", "\"beutl-mcp\"")
            .Replace("\"/old/home\"", "\"/home/user/.beutl\"")));
    }

    [TestCase("model = 'custom' # No final newline")]
    [TestCase("# Header\n\n[mcp_servers.beutl-agent]")]
    [TestCase("# Header\n\nmcp_servers = { }")]
    [TestCase("[mcp_servers.beutl-agent.env]\n# Keep this comment")]
    [TestCase("mcp_servers.beutl-agent = { url = 'http://127.0.0.1:59737/mcp', http_headers = { Authorization = 'Bearer old-token' } }")]
    [TestCase("mcp_servers.beutl-agent = { command = 'old', url = 'http://127.0.0.1:59737/mcp' }")]
    [TestCase("mcp_servers.beutl-agent = { command = 'old', args = ['--x'], env = { OLD = 'value' } }")]
    public async Task Additions_and_transport_changes_are_valid_and_idempotent(string original)
    {
        await File.WriteAllTextAsync(ConfigPath, original);

        await AgentToolkitInstaller.InstallAsync(Options, []);

        string updated = await File.ReadAllTextAsync(ConfigPath);
        var servers = (TomlTable)TomlSerializer.Deserialize<TomlTable>(updated)!["mcp_servers"];
        TomlTable server = Server(servers);
        Assert.Multiple(() =>
        {
            Assert.That(server["command"], Is.EqualTo(ServerCommand));
            Assert.That((TomlArray)server["args"], Is.Empty);
            Assert.That(Home(server), Is.EqualTo(ProfileHome));
            Assert.That(((TomlTable)server["env"]).ContainsKey("OLD"), Is.False);
            Assert.That(server.ContainsKey("url"), Is.False);
            Assert.That(server.ContainsKey("http_headers"), Is.False);
            Assert.That(updated, Does.Not.Contain("old-token"));
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(updated, @",\s*}"), Is.False,
                "Do not leave a trailing comma when removing inline settings; older TOML readers reject it.");
        });

        await AgentToolkitInstaller.InstallAsync(Options, []);

        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(updated));
    }

    [Test]
    public async Task Table_names_inside_unrelated_multiline_strings_are_not_edited()
    {
        const string original = """"
            # Documentation example

            developer_instructions = """
            [mcp_servers.beutl-agent]

            command = "keep this example"
            """

            [mcp_servers.beutl-agent]
            command = "beutl-mcp"
            args = []

            [mcp_servers.beutl-agent.env]
            BEUTL_HOME = "/old/home"
            """";
        await File.WriteAllTextAsync(ConfigPath, original);

        await AgentToolkitInstaller.InstallAsync(Options, []);

        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(original.Replace("\"/old/home\"", "\"/home/user/.beutl\"")));
    }

    [Test]
    public async Task Install_preserves_user_settings_and_foreign_servers_and_replaces_only_its_own_entry()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            # User settings
            model = "custom-model" # Keep this choice
            [mcp_servers.other]
            command = 'C:\tools\other.exe'
            args = ["--flag", "two words"]
            [mcp_servers.other.env]
            EXISTING = "value"
            [mcp_servers.beutl-live]
            url = "http://127.0.0.1:59737/mcp"
            [mcp_servers.beutl-live.http_headers]
            Authorization = "Bearer legacy-token"
            [mcp_servers."beutl-agent"]
            command = "obsolete"
            [mcp_servers."beutl-agent".env]
            OLD = "obsolete"
            [projects."/work/a.b"]
            trust_level = "trusted"
            """);

        AgentToolkitInstallResult result = await AgentToolkitInstaller.InstallAsync(Options, []);
        // A reinstall updates the profile rather than duplicating the TOML table.
        await AgentToolkitInstaller.InstallAsync(WithHome(Options, OtherHome), []);

        string text = await File.ReadAllTextAsync(ConfigPath);
        TomlTable root = TomlSerializer.Deserialize<TomlTable>(text)!;
        var servers = (TomlTable)root["mcp_servers"];
        TomlTable server = Server(servers);
        var other = (TomlTable)servers["other"];
        var legacyLive = (TomlTable)servers["beutl-live"];
        Assert.Multiple(() =>
        {
            Assert.That(result.McpConfigPath, Is.EqualTo(ConfigPath));
            Assert.That(result.InstalledFiles, Is.EqualTo(new[] { ConfigPath }));
            Assert.That(result.InstalledMcp, Is.True);
            Assert.That(server["command"], Is.EqualTo(ServerCommand));
            Assert.That(Home(server), Is.EqualTo(OtherHome));
            Assert.That(((TomlTable)server["env"]).ContainsKey("OLD"), Is.False);
            Assert.That(servers, Has.Count.EqualTo(3));
            // Entries the installer did not write under its own name, including a legacy direct
            // live entry a user may rely on, are left exactly as they were.
            Assert.That(legacyLive["url"], Is.EqualTo("http://127.0.0.1:59737/mcp"));
            Assert.That(((TomlTable)legacyLive["http_headers"])["Authorization"], Is.EqualTo("Bearer legacy-token"));
            Assert.That(other["command"], Is.EqualTo(@"C:\tools\other.exe"));
            Assert.That((TomlArray)other["args"], Is.EqualTo(new[] { "--flag", "two words" }));
            Assert.That(((TomlTable)other["env"])["EXISTING"], Is.EqualTo("value"));
            Assert.That(root["model"], Is.EqualTo("custom-model"));
            Assert.That(((TomlTable)((TomlTable)root["projects"])["/work/a.b"])["trust_level"], Is.EqualTo("trusted"));
            Assert.That(text, Does.Contain("# User settings").And.Contain("# Keep this choice"));
        });
    }

    [Test]
    public async Task Fresh_install_escapes_arguments_and_adds_the_workspace()
    {
        string[] arguments = ["--path", "a path with spaces", "quoted \"text\"", @"C:\video\日本語", "line\nbreak"];
        await AgentToolkitInstaller.InstallAsync(Options with
        {
            McpCommand = @"C:\Program Files\Beutl\mcp.exe",
            McpArguments = arguments,
            WorkspaceRoot = _root,
            McpEnvironment = new Dictionary<string, string> { ["EXTRA"] = "value", ["BEUTL_HOME"] = ProfileHome },
        }, []);

        var servers = (TomlTable)TomlSerializer.Deserialize<TomlTable>(await File.ReadAllTextAsync(ConfigPath))!["mcp_servers"];
        TomlTable server = Server(servers);
        Assert.That(File.Exists(Path.Combine(_root, ".mcp.json")), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(servers.Keys, Is.EqualTo(new[] { "beutl-agent" }));
            Assert.That(server["command"], Is.EqualTo(@"C:\Program Files\Beutl\mcp.exe"));
            Assert.That((TomlArray)server["args"], Is.EqualTo(arguments));
            Assert.That(((TomlTable)server["env"])["BEUTL_WORKSPACE"], Is.EqualTo(Path.GetFullPath(_root)));
            Assert.That(((TomlTable)server["env"])["EXTRA"], Is.EqualTo("value"));
            Assert.That(Home(server), Is.EqualTo(ProfileHome));
        });
    }

    [TestCase("mcp_servers.other = { url = 'https://example.com/mcp' }")]
    [TestCase("mcp_servers = { other = { url = 'https://example.com/mcp' } }")]
    [TestCase("[mcp_servers]\nother.url = 'https://example.com/mcp'")]
    public async Task Existing_inline_tables_and_dotted_keys_are_merged(string original)
    {
        await File.WriteAllTextAsync(ConfigPath, original);

        await AgentToolkitInstaller.InstallAsync(Options, []);

        var servers = (TomlTable)TomlSerializer.Deserialize<TomlTable>(await File.ReadAllTextAsync(ConfigPath))!["mcp_servers"];
        Assert.Multiple(() =>
        {
            Assert.That(((TomlTable)servers["other"])["url"], Is.EqualTo("https://example.com/mcp"));
            Assert.That(Server(servers)["command"], Is.EqualTo(ServerCommand));
        });
    }

    [TestCase("model = [")]
    [TestCase("mcp_servers = 'not a table'")]
    [TestCase("[[mcp_servers]]\nname = 'invalid'")]
    public async Task Invalid_config_is_reported_without_overwriting_it(string original)
    {
        await File.WriteAllTextAsync(ConfigPath, original);

        Assert.That(async () => await AgentToolkitInstaller.InstallAsync(Options, []), Throws.Exception);
        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(original));
    }

    [Test]
    public async Task Mcp_config_can_use_a_separate_codex_home()
    {
        string codexRoot = Path.Combine(_root, "custom-codex-home");
        AgentToolkitInstallResult result = await AgentToolkitInstaller.InstallAsync(Options with
        {
            McpConfigRoot = codexRoot,
            McpConfigFileName = "config.toml",
        }, []);

        Assert.That(result.McpConfigPath, Is.EqualTo(Path.Combine(codexRoot, "config.toml")));
        Assert.That(File.Exists(result.McpConfigPath), Is.True);
        Assert.That(File.Exists(ConfigPath), Is.False);
    }
}
