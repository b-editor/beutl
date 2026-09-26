using Beutl.AgentToolkit.Installation;
using Tomlyn;
using Tomlyn.Model;

namespace Beutl.AgentToolkit.Tests.Installation;

public sealed class CodexMcpConfigWriterTests
{
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

    private AgentToolkitInstallOptions Options => new()
    {
        AgentRoot = _root,
        InstallSkills = false,
        InstallSubagents = false,
        InstallStdioMcp = false,
        InstallLiveMcp = true,
        McpConfigFileName = Path.Combine(".codex", "config.toml"),
        McpConfigFormat = McpConfigFormat.CodexToml,
        McpServersPropertyName = "mcp_servers",
        LiveMcpUri = new Uri("http://127.0.0.1:59737/mcp"),
        LiveMcpHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer test-token" },
    };

    [TestCase("mcp_servers", "team.stdio", "server name")]
    [TestCase("mcp.servers name", "stdio \"quoted\"", "live\\server")]
    public async Task Non_bare_key_segments_are_quoted_and_reinstall_matches_the_same_servers(
        string serversProperty, string stdioName, string liveName)
    {
        AgentToolkitInstallOptions options = Options with
        {
            McpServersPropertyName = serversProperty,
            StdioMcpServerName = stdioName,
            LiveMcpServerName = liveName,
            InstallStdioMcp = true,
            StdioMcpCommand = "beutl-mcp",
        };
        await AgentToolkitInstaller.InstallAsync(options, []);
        await AgentToolkitInstaller.InstallAsync(options with { LiveMcpUri = new Uri("http://127.0.0.1:59738/mcp") }, []);

        TomlTable root = TomlSerializer.Deserialize<TomlTable>(await File.ReadAllTextAsync(ConfigPath))!;
        var servers = (TomlTable)root[serversProperty];
        Assert.Multiple(() =>
        {
            Assert.That(root.Keys, Is.EqualTo(new[] { serversProperty }));
            Assert.That(servers.Keys, Is.EquivalentTo(new[] { stdioName, liveName }));
            Assert.That(((TomlTable)servers[stdioName])["command"], Is.EqualTo("beutl-mcp"));
            Assert.That(((TomlTable)servers[liveName])["url"], Is.EqualTo("http://127.0.0.1:59738/mcp"));
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
        AgentToolkitInstallOptions options = unchanged ? Options
            : Options with { LiveMcpUri = new Uri("http://127.0.0.1:59738/mcp") };

        await AgentToolkitInstaller.InstallAsync(options, []);

        Assert.That(File.GetUnixFileMode(ConfigPath), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        var servers = (TomlTable)TomlSerializer.Deserialize<TomlTable>(await File.ReadAllTextAsync(ConfigPath))!["mcp_servers"];
        Assert.That(((TomlTable)servers["beutl-live"])["url"], Is.EqualTo(options.LiveMcpUri!.ToString()));
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
            Assert.ThrowsAsync<OperationCanceledException>(Write);
        else
            Assert.ThrowsAsync<IOException>(Write);
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
        Assert.That(((TomlTable)root["mcp_servers"]).ContainsKey("beutl-live"), Is.True);
        Assert.That(File.GetUnixFileMode(target), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
    }

    [Test]
    public async Task Fresh_live_install_uses_explicit_server_and_header_tables()
    {
        await AgentToolkitInstaller.InstallAsync(Options, []);

        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(
            "[mcp_servers.beutl-live]\nurl = \"http://127.0.0.1:59737/mcp\"\n\n"
            + "[mcp_servers.beutl-live.http_headers]\nAuthorization = \"Bearer test-token\"\n"));
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
            "mcp_servers.beutl-live.url = \"http://127.0.0.1:59737/mcp\" # Live URL",
            "mcp_servers.beutl-live.http_headers.Authorization = \"Bearer old-token\"",
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
            Assert.That(updated, Does.Contain($"[mcp_servers.beutl-live]{newline}url = \"http://127.0.0.1:59737/mcp\"{newline}"));
            Assert.That(updated, Does.Contain($"[mcp_servers.beutl-live.http_headers]{newline}Authorization = \"Bearer test-token\"{newline}"));
            Assert.That(updated, Does.Not.Contain("mcp_servers.beutl-live.url ="));
            Assert.That(updated, Does.Not.Contain("mcp_servers.beutl-live.http_headers.Authorization ="));
            Assert.That(updated, Does.Contain($"model = 'custom-model'{newline}{newline}{newline}"));
            Assert.That(updated, Does.Contain("# Live URL"));
            Assert.That(updated, Does.Contain($"[mcp_servers.other]{newline}command   = 'keep this'{newline}{newline}"));
        });

        await AgentToolkitInstaller.InstallAsync(Options, []);

        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(updated));
    }

    [Test]
    public async Task Adding_headers_to_an_existing_server_creates_a_header_table()
    {
        const string original = "[mcp_servers.beutl-live]\nurl = 'http://127.0.0.1:59737/mcp'\n\n";
        await File.WriteAllTextAsync(ConfigPath, original);

        await AgentToolkitInstaller.InstallAsync(Options, []);

        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(original
            + "[mcp_servers.beutl-live.http_headers]\nAuthorization = \"Bearer test-token\"\n"));
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

        string inserted = $"{newline}[mcp_servers.beutl-live]{newline}url = \"http://127.0.0.1:59737/mcp\"{newline}"
                          + $"{newline}[mcp_servers.beutl-live.http_headers]{newline}Authorization = \"Bearer test-token\"{newline}";
        string expected = prefix + inserted + suffix;
        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(expected));

        await AgentToolkitInstaller.InstallAsync(Options, []);

        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(expected));
    }

    [Test]
    public async Task New_header_table_follows_an_updated_server_before_the_next_settings_table()
    {
        const string original = "[mcp_servers.beutl-live]\ncommand = 'old'\n\n# Project settings\n[projects.example]\ntrust_level = 'trusted'\n";
        await File.WriteAllTextAsync(ConfigPath, original);

        await AgentToolkitInstaller.InstallAsync(Options, []);

        string updated = await File.ReadAllTextAsync(ConfigPath);
        int server = updated.IndexOf("[mcp_servers.beutl-live]", StringComparison.Ordinal);
        int url = updated.IndexOf("url = ", StringComparison.Ordinal);
        int headers = updated.IndexOf("[mcp_servers.beutl-live.http_headers]", StringComparison.Ordinal);
        int project = updated.IndexOf("# Project settings", StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(url, Is.GreaterThan(server).And.LessThan(headers));
            Assert.That(headers, Is.GreaterThan(server).And.LessThan(project));
            Assert.That(updated, Does.EndWith("\n\n# Project settings\n[projects.example]\ntrust_level = 'trusted'\n"));
            var servers = (TomlTable)TomlSerializer.Deserialize<TomlTable>(updated)!["mcp_servers"];
            Assert.That(((TomlTable)servers["beutl-live"])["url"], Is.EqualTo(Options.LiveMcpUri!.ToString()));
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
        Assert.That(updated.IndexOf("[mcp_servers.beutl-live]", StringComparison.Ordinal), Is.GreaterThanOrEqualTo(original.Length));
        var servers = (TomlTable)TomlSerializer.Deserialize<TomlTable>(updated)!["mcp_servers"];
        Assert.That(((TomlTable)servers["beutl-live"])["url"], Is.EqualTo(Options.LiveMcpUri!.ToString()));
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
            "# Live connection",
            "[mcp_servers.\"beutl-live\"]  # Keep header",
            "url  = \"http://127.0.0.1:59737/mcp\"   # Keep comment",
            "", "",
            "[mcp_servers.'beutl-live'.http_headers]",
            "Authorization = \"Bearer old-token\"",
            "", "",
            "[projects.\"/work/a.b\"]",
            "trust_level  = 'trusted'",
            "", "",
        ]);
        await File.WriteAllTextAsync(ConfigPath, original);

        await AgentToolkitInstaller.InstallAsync(Options, []);

        string expected = original.Replace("Bearer old-token", "Bearer test-token");
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

    [TestCase("mcp_servers = { other = { command = 'other' },  beutl-live = { url = 'http://127.0.0.1:59737/mcp', http_headers = { Authorization = \"Bearer old-token\" } } }\n\n")]
    public async Task Inline_server_updates_preserve_original_spacing(string original)
    {
        await File.WriteAllTextAsync(ConfigPath, original);

        await AgentToolkitInstaller.InstallAsync(Options, []);

        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(original.Replace("Bearer old-token", "Bearer test-token")));
    }

    [TestCase("model = 'custom' # No final newline")]
    [TestCase("# Header\n\n[mcp_servers.beutl-live]")]
    [TestCase("# Header\n\nmcp_servers = { }")]
    [TestCase("[mcp_servers.beutl-live.http_headers]\n# Keep this comment")]
    [TestCase("mcp_servers.beutl-live = { command = 'old', args = [], env = { OLD = 'value' } }")]
    [TestCase("mcp_servers.beutl-live = { url = 'http://127.0.0.1:59737/mcp', command = 'old' }")]
    [TestCase("mcp_servers.beutl-live = { command = 'old', url = 'http://127.0.0.1:59737/mcp' }")]
    [TestCase("mcp_servers.beutl-live = { url = 'http://127.0.0.1:59737/mcp', http_headers = { Authorization = 'Bearer test-token' }, command = 'old' }")]
    public async Task Additions_and_transport_changes_are_valid_and_idempotent(string original)
    {
        await File.WriteAllTextAsync(ConfigPath, original);

        await AgentToolkitInstaller.InstallAsync(Options, []);

        string updated = await File.ReadAllTextAsync(ConfigPath);
        var servers = (TomlTable)TomlSerializer.Deserialize<TomlTable>(updated)!["mcp_servers"];
        var live = (TomlTable)servers["beutl-live"];
        Assert.Multiple(() =>
        {
            Assert.That(live["url"], Is.EqualTo(Options.LiveMcpUri!.ToString()));
            Assert.That(((TomlTable)live["http_headers"])["Authorization"], Is.EqualTo("Bearer test-token"));
            Assert.That(live.ContainsKey("command"), Is.False);
            Assert.That(live.ContainsKey("args"), Is.False);
            Assert.That(live.ContainsKey("env"), Is.False);
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
            [mcp_servers.beutl-live]

            url = "keep this example"
            """

            [mcp_servers.beutl-live]
            url = 'http://127.0.0.1:59737/mcp'

            [mcp_servers.beutl-live.http_headers]
            Authorization = "Bearer old-token"
            """";
        await File.WriteAllTextAsync(ConfigPath, original);

        await AgentToolkitInstaller.InstallAsync(Options, []);

        Assert.That(await File.ReadAllTextAsync(ConfigPath), Is.EqualTo(original.Replace("Bearer old-token", "Bearer test-token")));
    }

    [Test]
    public async Task Live_install_preserves_user_settings_and_replaces_only_the_selected_server()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            # User settings
            model = "custom-model" # Keep this choice
            [mcp_servers.other]
            command = 'C:\tools\other.exe'
            args = ["--flag", "two words"]
            [mcp_servers.other.env]
            EXISTING = "value"
            [mcp_servers.beutl-agent]
            command = "keep-stdio"
            [mcp_servers."beutl-live"]
            command = "obsolete-transport"
            [mcp_servers."beutl-live".env]
            OLD = "obsolete"
            [projects."/work/a.b"]
            trust_level = "trusted"
            """);

        AgentToolkitInstallResult result = await AgentToolkitInstaller.InstallAsync(Options, []);
        // A reinstall updates the URL and token rather than duplicating the TOML table.
        await AgentToolkitInstaller.InstallAsync(Options with
        {
            LiveMcpUri = new Uri("http://127.0.0.1:59738/mcp"),
            LiveMcpHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer new-token" },
        }, []);

        string text = await File.ReadAllTextAsync(ConfigPath);
        TomlTable root = TomlSerializer.Deserialize<TomlTable>(text)!;
        var servers = (TomlTable)root["mcp_servers"];
        var live = (TomlTable)servers["beutl-live"];
        var other = (TomlTable)servers["other"];
        Assert.Multiple(() =>
        {
            Assert.That(result.McpConfigPath, Is.EqualTo(ConfigPath));
            Assert.That(result.InstalledFiles, Is.EqualTo(new[] { ConfigPath }));
            Assert.That(result.InstalledLiveMcp, Is.True);
            Assert.That(result.InstalledStdioMcp, Is.False);
            Assert.That(live["url"], Is.EqualTo("http://127.0.0.1:59738/mcp"));
            Assert.That(((TomlTable)live["http_headers"])["Authorization"], Is.EqualTo("Bearer new-token"));
            Assert.That(live.ContainsKey("command"), Is.False);
            Assert.That(live.ContainsKey("env"), Is.False);
            Assert.That(live.ContainsKey("headers"), Is.False);
            Assert.That(servers, Has.Count.EqualTo(3));
            Assert.That(((TomlTable)servers["beutl-agent"])["command"], Is.EqualTo("keep-stdio"));
            Assert.That(other["command"], Is.EqualTo(@"C:\tools\other.exe"));
            Assert.That((TomlArray)other["args"], Is.EqualTo(new[] { "--flag", "two words" }));
            Assert.That(((TomlTable)other["env"])["EXISTING"], Is.EqualTo("value"));
            Assert.That(root["model"], Is.EqualTo("custom-model"));
            Assert.That(((TomlTable)((TomlTable)root["projects"])["/work/a.b"])["trust_level"], Is.EqualTo("trusted"));
            Assert.That(text, Does.Contain("# User settings").And.Contain("# Keep this choice"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Fresh_install_writes_only_selected_transports_with_escaped_arguments(bool installStdio)
    {
        string[] arguments = ["--path", "a path with spaces", "quoted \"text\"", @"C:\video\日本語", "line\nbreak"];
        await AgentToolkitInstaller.InstallAsync(Options with
        {
            InstallStdioMcp = installStdio,
            StdioMcpCommand = @"C:\Program Files\Beutl\mcp.exe",
            StdioMcpArguments = arguments,
            WorkspaceRoot = _root,
            StdioMcpEnvironment = new Dictionary<string, string> { ["EXTRA"] = "value" },
        }, []);

        var servers = (TomlTable)TomlSerializer.Deserialize<TomlTable>(await File.ReadAllTextAsync(ConfigPath))!["mcp_servers"];
        Assert.That(servers.ContainsKey("beutl-agent"), Is.EqualTo(installStdio));
        Assert.That(servers.ContainsKey("beutl-live"), Is.True);
        Assert.That(File.Exists(Path.Combine(_root, ".mcp.json")), Is.False);
        if (installStdio)
        {
            var stdio = (TomlTable)servers["beutl-agent"];
            Assert.Multiple(() =>
            {
                Assert.That(stdio["command"], Is.EqualTo(@"C:\Program Files\Beutl\mcp.exe"));
                Assert.That((TomlArray)stdio["args"], Is.EqualTo(arguments));
                Assert.That(((TomlTable)stdio["env"])["BEUTL_WORKSPACE"], Is.EqualTo(Path.GetFullPath(_root)));
                Assert.That(((TomlTable)stdio["env"])["EXTRA"], Is.EqualTo("value"));
            });
        }
    }

    [Test]
    public async Task Stdio_only_install_preserves_existing_live_registration()
    {
        await AgentToolkitInstaller.InstallAsync(Options, []);
        await AgentToolkitInstaller.InstallAsync(Options with
        {
            InstallStdioMcp = true,
            InstallLiveMcp = false,
            StdioMcpCommand = "beutl-mcp",
            WorkspaceRoot = _root,
            StdioMcpEnvironment = new Dictionary<string, string> { ["BEUTL_WORKSPACE"] = "override" },
        }, []);

        var servers = (TomlTable)TomlSerializer.Deserialize<TomlTable>(await File.ReadAllTextAsync(ConfigPath))!["mcp_servers"];
        Assert.Multiple(() =>
        {
            Assert.That(((TomlTable)servers["beutl-live"])["url"], Is.EqualTo(Options.LiveMcpUri!.ToString()));
            Assert.That(((TomlTable)((TomlTable)servers["beutl-agent"])["env"])["BEUTL_WORKSPACE"], Is.EqualTo("override"));
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
            Assert.That(((TomlTable)servers["beutl-live"])["url"], Is.EqualTo(Options.LiveMcpUri!.ToString()));
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
