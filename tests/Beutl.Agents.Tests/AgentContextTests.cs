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
