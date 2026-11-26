using Beutl.AgentToolkit.Installation;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class McpSkillTests
{
    [Test]
    public async Task Stdio_server_lists_and_serves_the_bundled_skills()
    {
        await using var server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        var client = server.Client;
        var bundled = BundledAgentToolkitAssets.Load()
            .Where(asset => asset.Kind == AgentToolkitAssetKind.Skill)
            .ToLookup(asset => asset.RelativePath.Split('/', 2)[0], StringComparer.Ordinal);

#pragma warning disable MCPEXP005 // The SDK marks its Skills extension experimental.
        Assert.That(client.ServerCapabilities.SupportsSkills(), Is.True);
        Assert.That(client.ServerCapabilities.Resources, Is.Not.Null);

        var skills = await client.ListSkillsAsync();
        var resources = await client.ListResourcesAsync();
        Assert.That(skills.Select(skill => skill.Name), Is.EquivalentTo(bundled.Select(group => group.Key)));
        Assert.That(resources.Select(resource => resource.Uri),
            Is.SupersetOf(bundled.SelectMany(group => group).Select(asset => $"skill://beutl/{asset.RelativePath}")));

        var guide = ToolResultReader.Read<GettingStartedResponse>(await client.CallToolAsync("get_started")).Value!;
        foreach (var listed in skills)
        {
            string name = listed.Name!;
            string skillUri = $"skill://beutl/{name}/SKILL.md";
            Assert.That(listed.Uri, Is.EqualTo(skillUri));
            Assert.That(guide.RecommendedSkills.Single(skill => skill.Name == name).HowToLoad,
                Does.Contain(skillUri));

            var entry = await client.GetSkillAsync(skillUri);
            Assert.That(entry.Description, Is.EqualTo(listed.Description).And.Not.Empty);
            var files = bundled[name].ToDictionary(asset => $"skill://beutl/{asset.RelativePath}");
            Assert.That(listed.Resources.Entries!.Select(file => file.Uri), Is.EquivalentTo(files.Keys));
            foreach (var (uri, asset) in files)
            {
                // The client verifies the manifest's digest and size against the bytes returned by resources/read.
                var result = await client.ReadSkillResourceAsync(entry, uri);
                Assert.That(result.Contents.OfType<TextResourceContents>().Single().Text, Is.EqualTo(asset.Content));
            }
        }
#pragma warning restore MCPEXP005
    }
}
