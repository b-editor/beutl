using Beutl.AgentToolkit.Installation;

namespace Beutl.AgentToolkit.Tests.Installation;

[TestFixture]
public sealed class McpCliCommandsTests
{
    private static readonly Dictionary<string, string> s_environment = new()
    {
        ["BEUTL_WORKSPACE"] = "/videos",
    };

    [Test]
    public void Claude_code_global_stdio_uses_user_scope()
    {
        McpCliCommand? command = AgentMcpCliCommands.BuildStdio(
            "claude-code", AgentInstallScope.Global, "beutl-agent", "beutl-mcp", ["--stdio"],
            new Dictionary<string, string> { ["BEUTL_WORKSPACE"] = "/videos", ["BEUTL_HOME"] = "/home/user/.beutl" });

        Assert.That(command, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(command!.Executable, Is.EqualTo("claude"));
            Assert.That(command.Arguments, Is.EqualTo(new[]
            {
                "mcp", "add", "--scope", "user", "beutl-agent",
                "--env", "BEUTL_WORKSPACE=/videos",
                "--env", "BEUTL_HOME=/home/user/.beutl",
                "--", "beutl-mcp", "--stdio",
            }));
        });
    }

    [Test]
    public void Remove_commands_exist_for_every_cli_supported_combination()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                AgentMcpCliCommands.BuildRemove("claude-code", AgentInstallScope.Global, "beutl-agent")!.Arguments,
                Is.EqualTo(new[] { "mcp", "remove", "--scope", "user", "beutl-agent" }));
        });
    }

    [Test]
    public void Unsupported_combinations_return_null()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                AgentMcpCliCommands.BuildStdio(
                    "claude-code", AgentInstallScope.Project, "s", "cmd", [], s_environment),
                Is.Null);
            Assert.That(
                AgentMcpCliCommands.BuildStdio("cursor", AgentInstallScope.Global, "s", "cmd", [], s_environment),
                Is.Null);
            Assert.That(AgentMcpCliCommands.SupportsStdio("custom", AgentInstallScope.Global), Is.False);
        });
    }

    [Test]
    public void Display_string_quotes_arguments_with_spaces()
    {
        var command = new McpCliCommand("claude", ["mcp", "add", "my server", "--", "/Applications/My App/mcp"]);

        Assert.That(
            command.ToDisplayString(),
            Is.EqualTo("claude mcp add \"my server\" -- \"/Applications/My App/mcp\""));
    }

    [Test]
    public async Task Runner_reports_success_and_failure()
    {
        McpCliResult ok = await McpCliRunner.RunAsync(new McpCliCommand("dotnet", ["--version"]));
        McpCliResult missing = await McpCliRunner.RunAsync(
            new McpCliCommand("beutl-no-such-executable-1f4a", []));

        Assert.Multiple(() =>
        {
            Assert.That(ok.Success, Is.True, ok.Output);
            Assert.That(missing.Success, Is.False);
            Assert.That(missing.Output, Is.Not.Empty);
        });
    }
}
