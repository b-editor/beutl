using Beutl.AgentToolkit.Installation;

namespace Beutl.AgentToolkit.Tests.Installation;

[TestFixture]
public sealed class CodexSubagentConverterTests
{
    [Test]
    public void Converts_frontmatter_and_body_into_codex_toml()
    {
        const string markdown = """
            ---
            name: beutl-agent-timeline-builder
            description: Builds Beutl timelines. Use for "scoped" timeline tasks.
            ---

            You are a Beutl timeline-building specialist.

            ## Responsibilities

            - Convert shot-list timing into `Element` structure.
            """;

        string toml = CodexSubagentConverter.Convert(markdown, "fallback");

        Assert.Multiple(() =>
        {
            Assert.That(toml, Does.StartWith("name = \"beutl-agent-timeline-builder\""));
            Assert.That(
                toml,
                Does.Contain("description = \"Builds Beutl timelines. Use for \\\"scoped\\\" timeline tasks.\""));
            Assert.That(toml, Does.Contain("developer_instructions = '''"));
            Assert.That(toml, Does.Contain("You are a Beutl timeline-building specialist."));
            Assert.That(toml, Does.Contain("- Convert shot-list timing into `Element` structure."));
            Assert.That(toml, Does.Not.Contain("---"));
        });
    }

    [Test]
    public void Missing_frontmatter_falls_back_to_the_file_name()
    {
        string toml = CodexSubagentConverter.Convert("Just a body.", "beutl-agent-quality-reviewer");

        Assert.Multiple(() =>
        {
            Assert.That(toml, Does.StartWith("name = \"beutl-agent-quality-reviewer\""));
            Assert.That(toml, Does.Contain("description = \"\""));
            Assert.That(toml, Does.Contain("Just a body."));
        });
    }

    [Test]
    public void Body_containing_the_literal_delimiter_uses_an_escaped_block()
    {
        string toml = CodexSubagentConverter.Convert(
            """
            ---
            name: x
            description: y
            ---
            Body with ''' inside and a back\slash.
            """,
            "x");

        Assert.Multiple(() =>
        {
            Assert.That(toml, Does.Contain("developer_instructions = \"\"\""));
            Assert.That(toml, Does.Contain("back\\\\slash"));
        });
    }

    [Test]
    public void Every_bundled_subagent_converts_with_its_own_name()
    {
        foreach (AgentToolkitAsset asset in BundledAgentToolkitAssets.Load()
                     .Where(a => a.Kind == AgentToolkitAssetKind.Subagent))
        {
            string toml = CodexSubagentConverter.Convert(
                asset.Content, Path.GetFileNameWithoutExtension(asset.RelativePath));

            Assert.Multiple(() =>
            {
                Assert.That(
                    toml,
                    Does.StartWith($"name = \"{Path.GetFileNameWithoutExtension(asset.RelativePath)}\""),
                    asset.RelativePath);
                Assert.That(toml, Does.Contain("developer_instructions = '''"), asset.RelativePath);
            });
        }
    }
}
