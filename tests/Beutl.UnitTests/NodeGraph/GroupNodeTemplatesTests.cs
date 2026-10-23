using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.JsonConverters;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Generative;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Generative;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.Serialization;
using static Beutl.UnitTests.NodeGraph.NodeGraphTestHelpers;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public sealed class GroupNodeTemplatesTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "beutl-node-templates-" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public void ATemplateKeepsTheWorkflowAndLeavesTheResultsBehind()
    {
        var store = new GroupNodeTemplates(_directory);
        var (group, prompt, image) = CreateGroup();

        GroupNodeTemplate saved = store.Save(group, "Cat pipeline")!;
        GroupNode copy = store.Instantiate(saved)!;

        AiPromptNode copiedPrompt = copy.Group.Nodes.OfType<AiPromptNode>().Single();
        AiImageGenerationNode copiedImage = copy.Group.Nodes.OfType<AiImageGenerationNode>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(store.List().Select(t => t.Name), Is.EqualTo(new[] { "Cat pipeline" }));
            Assert.That(copiedPrompt.Prompt.Property!.GetValue(), Is.EqualTo("a cat"));
            Assert.That(copiedImage.Prompt.Connection.Value?.Output.Value, Is.SameAs(copiedPrompt.Output),
                "The connection inside the group points at the copy, not the original.");
            Assert.That(copiedImage.Generations, Is.Empty, "Results are not part of the workflow.");
            Assert.That(copiedImage.RequestKeySeed, Is.Not.EqualTo(image.RequestKeySeed));
            Assert.That(copiedImage.Id, Is.Not.EqualTo(image.Id));
            Assert.That(copy.Id, Is.Not.EqualTo(group.Id));
        });
    }

    [Test]
    public void ATemplateCanBeAddedTwiceWithoutSharingAnything()
    {
        var store = new GroupNodeTemplates(_directory);
        var (group, _, _) = CreateGroup();
        GroupNodeTemplate saved = store.Save(group, "Cat pipeline")!;

        GroupNode first = store.Instantiate(saved)!;
        GroupNode second = store.Instantiate(saved)!;
        var graph = new GraphModel();
        graph.Nodes.Add(first);
        graph.Nodes.Add(second);

        AiImageGenerationNode a = first.Group.Nodes.OfType<AiImageGenerationNode>().Single();
        AiImageGenerationNode b = second.Group.Nodes.OfType<AiImageGenerationNode>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(a.Id, Is.Not.EqualTo(b.Id));
            Assert.That(a.Prompt.Id, Is.Not.EqualTo(b.Prompt.Id));
            Assert.That(a.RequestKeySeed, Is.Not.EqualTo(b.RequestKeySeed),
                "Two copies must not answer each other's paid requests.");
            Assert.That(b.Prompt.Connection.Value?.Output.Value,
                Is.SameAs(second.Group.Nodes.OfType<AiPromptNode>().Single().Output));
            Assert.That(Ids(first).Intersect(Ids(second)), Is.Empty,
                "No object anywhere in the two copies shares an identifier.");
        });
    }

    private static HashSet<string> Ids(GroupNode group)
    {
        string json = CoreSerializer.SerializeToJsonObject(group).ToJsonString();
        return System.Text.RegularExpressions.Regex
            .Matches(json, "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
            .Select(match => match.Value)
            .Where(id => id != Guid.Empty.ToString())
            .ToHashSet();
    }

    // A template records no version, and one from before 2.0.0-preview.9 names a nested time port over TimeSpan (#2885).
    [Test]
    public void ATemplateSavedBeforeFlickCountsKeepsTheConnectionOfANestedTimePort()
    {
        var store = new GroupNodeTemplates(_directory);
        var group = new GroupNode();
        var factory = new FactoryNode<DrawableBrush>();
        var video = new SourceVideo();
        factory.Object.Drawable.Value = video;
        group.Group.Nodes.Add(factory);
        INestedInputPort port = Find(factory, video.OffsetPosition);
        Connection connection = group.Group.Connect(port, Source(group.Group, 2f));
        GroupNodeTemplate saved;
        using (MediaTimeJsonConverter.WriteTimeSpanStrings())
            saved = store.Save(group, "Video offset")!;
        string text = File.ReadAllText(saved.FilePath);
        Assert.That(text, Does.Contain("[Beutl.Core]Beutl:MediaTime"));
        File.WriteAllText(saved.FilePath, text.Replace("[Beutl.Core]Beutl:MediaTime", "[System.Private.CoreLib]System:TimeSpan"));

        GroupNode loaded = store.Load(saved)!;
        var loadedVideo = (SourceVideo)loaded.Group.Nodes.OfType<FactoryNode<DrawableBrush>>().Single().Object.Drawable.Value!;

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Group.AllConnections.Single().Id, Is.EqualTo(connection.Id));
            Assert.That(loaded.Group.FindNodePort(port.Id), Is.InstanceOf<NestedInputPort<MediaTime>>());
            Assert.That(loadedVideo.OffsetPosition.Expression, Is.InstanceOf<NodePortExpression<MediaTime>>());
        });
    }

    [TestCase("")]
    [TestCase("a/b")]
    [TestCase("..")]
    public void ANameThatCannotBeAFileIsRefused(string name)
    {
        var (group, _, _) = CreateGroup();
        Assert.That(new GroupNodeTemplates(_directory).Save(group, name), Is.Null);
    }

    [Test]
    public void SavingUnderATakenNameKeepsBoth()
    {
        var store = new GroupNodeTemplates(_directory);
        var (group, _, _) = CreateGroup();
        store.Save(group, "Cat");
        store.Save(group, "Cat");
        Assert.That(store.List().Select(t => t.Name), Is.EqualTo(new[] { "Cat", "Cat (2)" }));
    }

    [TestCase("[]")]
    [TestCase("null")]
    public void ATemplateThatIsNotAnObjectFailsToLoadInsteadOfThrowing(string content)
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "Broken.json");
        File.WriteAllText(path, content);

        Assert.That(new GroupNodeTemplates(_directory).Instantiate(new GroupNodeTemplate("Broken", path)), Is.Null);
    }

    private static (GroupNode Group, AiPromptNode Prompt, AiImageGenerationNode Image) CreateGroup()
    {
        var group = new GroupNode();
        var prompt = new AiPromptNode();
        prompt.Prompt.Property!.SetValue("a cat");
        var image = new AiImageGenerationNode();
        group.Group.Nodes.Add(prompt);
        group.Group.Nodes.Add(image);
        group.Group.Connect(image.Prompt, prompt.Output);
        image.Generations.Add(new GenerationRecord { Summary = "kept result" });
        return (group, prompt, image);
    }
}
