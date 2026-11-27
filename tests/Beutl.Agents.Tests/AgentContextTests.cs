using System.Text.Json;

namespace Beutl.Agents.Tests;

public sealed class AgentContextTests
{
    [Test]
    public void SnapshotsWithoutMentionsKeepTheExistingJsonShape()
    {
        var snapshot = new AgentContextSnapshot("instance", "project.bproj", "Demo", null, null, null, null, null,
            null, null, [], []);
        var json = AgentJson.ToElement(snapshot);

        Assert.That(json.TryGetProperty("mentions", out _), Is.False);
        Assert.That(AgentJson.Read<AgentContextSnapshot>(json).Mentions, Is.Null);
    }

    [Test]
    public void LegacyElementMentionsKeepTheirJsonShape()
    {
        using var json = JsonDocument.Parse("""
            {"kind":"element","name":"Title","targetId":"element-id","token":"@Title","sceneId":"scene-id"}
            """);
        var mention = AgentJson.Read<AgentContextMention>(json.RootElement);

        Assert.That(mention, Is.EqualTo(new AgentContextMention(AgentContextMentionKind.Element, "Title",
            "element-id", Token: "@Title", SceneId: "scene-id")));
        Assert.That(JsonElement.DeepEquals(AgentJson.ToElement(mention), json.RootElement), Is.True);
    }

    [Test]
    public void MentionTokensRequireAWhitespaceBoundaryAndCanFilterByRelativePath()
    {
        Assert.That(AgentComposerToken.TryFind("user@example.com", 9, out _), Is.False);
        const string prompt = "look at @assets/clip.mp4 now";
        int caret = prompt.IndexOf("clip.mp4", StringComparison.Ordinal) + 4;
        Assert.That(AgentComposerToken.TryFind(prompt, caret, out var token), Is.True);
        Assert.That(token.Prefix, Is.EqualTo("assets/clip"));
        Assert.That(prompt[token.Start..token.End], Is.EqualTo("@assets/clip.mp4"));
        Assert.That(AgentComposerToken.InvokedNames("@assets/clip.mp4 /review $edit"), Is.EqualTo(new[] { "review", "edit" }));
    }
}
