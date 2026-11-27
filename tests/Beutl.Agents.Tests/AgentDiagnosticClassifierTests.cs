using Beutl.Agents;

namespace Beutl.Agents.Tests;

public sealed class AgentDiagnosticClassifierTests
{
    [TestCase("API Error: 401 authentication_error", AgentDiagnosticKind.AuthRequired, "codex login")]
    [TestCase("Not logged in", AgentDiagnosticKind.AuthRequired, "codex login")]
    [TestCase("Missing API key", AgentDiagnosticKind.AuthRequired, "codex login")]
    [TestCase("token_expired", AgentDiagnosticKind.AuthRequired, "codex login")]
    [TestCase("HTTP 429 Too many requests", AgentDiagnosticKind.RateLimited, null)]
    [TestCase("You have exceeded your usage limit.", AgentDiagnosticKind.RateLimited, null)]
    [TestCase("The Beutl MCP launcher could not be found.", AgentDiagnosticKind.McpUnavailable, null)]
    [TestCase("Could not find an executable for codex.", AgentDiagnosticKind.CliMissing, "npm install -g @openai/codex")]
    [TestCase("Unsupported CLI version.", AgentDiagnosticKind.OutdatedCli, "npm install -g @openai/codex@latest")]
    [TestCase("The provider closed stdout.", AgentDiagnosticKind.Generic, null)]
    public void ClassifiesFailuresAndPreservesRawDetails(string error, AgentDiagnosticKind kind, string? command)
    {
        var diagnostic = AgentDiagnosticClassifier.FromFailure(error, AgentProviderDescriptor.Codex);
        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.Kind, Is.EqualTo(kind));
            Assert.That(diagnostic.Command, Is.EqualTo(command));
            Assert.That(diagnostic.RawMessage, Is.EqualTo(error));
        });
    }

    [Test]
    public void ProvidesTheDetectedAndMinimumVersionsForAnOutdatedProbe()
    {
        var probe = new AgentProviderProbeResult(AgentProviderDescriptor.ClaudeCode, "claude", null, null, "2.1.0-beta.1");
        var diagnostic = AgentDiagnosticClassifier.FromProbe(probe);
        Assert.That(diagnostic, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(diagnostic!.Kind, Is.EqualTo(AgentDiagnosticKind.OutdatedCli));
            Assert.That(diagnostic.CliVersion, Is.EqualTo("2.1.0-beta.1"));
            Assert.That(diagnostic.MinimumVersion, Is.EqualTo(new Version(2, 1, 0)));
            Assert.That(diagnostic.Command, Is.EqualTo("claude update"));
            Assert.That(AgentDiagnosticClassifier.FromProbe(probe with { CliVersion = "2.1.0" }), Is.Null);
        });
    }
}
