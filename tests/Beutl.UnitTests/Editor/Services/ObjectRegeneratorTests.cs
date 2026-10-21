using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Generative;
using Beutl.NodeGraph.Nodes.Generative;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ObjectRegeneratorTests
{
    [Test]
    public void Regenerate_AssignsNewIdsToEmbeddedChildren()
    {
        // Element has a Uri, so the default ReadWrite mode would emit only a file
        // reference and hide child Ids. Embed mode expands them so Ids get rewritten.
        var src = new Element
        {
            Start = TimeSpan.FromSeconds(0),
            Length = TimeSpan.FromSeconds(1),
            ZIndex = 0,
            Uri = new Uri(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.belm")),
        };
        var child = new PortalObject();
        src.AddObject(child);
        Guid srcId = src.Id;
        Guid childId = child.Id;

        ObjectRegenerator.Regenerate(src, out Element regenerated);

        Assert.That(regenerated.Id, Is.Not.EqualTo(srcId));
        Assert.That(regenerated.Objects.Count, Is.EqualTo(1), "Embed mode should re-materialize child objects.");
        Guid regeneratedChildId = regenerated.Objects[0].Id;
        Assert.That(regeneratedChildId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(regeneratedChildId, Is.Not.EqualTo(childId));
    }

    [Test]
    public void Regenerate_Array_PreservesOrderAndAssignsNewIds()
    {
        // PlaceDuplicates relies on positional zip (sourceElements[i] -> newElements[i]).
        // If Regenerate reorders the array, group remap silently corrupts.
        var a = new Element
        {
            Start = TimeSpan.FromSeconds(0),
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.belm")),
        };
        var b = new Element
        {
            Start = TimeSpan.FromSeconds(5),
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.belm")),
        };
        var c = new Element
        {
            Start = TimeSpan.FromSeconds(10),
            Length = TimeSpan.FromSeconds(3),
            Uri = new Uri(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.belm")),
        };
        Guid[] originalIds = [a.Id, b.Id, c.Id];

        ObjectRegenerator.Regenerate([a, b, c], out Element[] regenerated);

        Assert.That(regenerated.Length, Is.EqualTo(3));

        Assert.That(regenerated[0].Start, Is.EqualTo(a.Start));
        Assert.That(regenerated[1].Start, Is.EqualTo(b.Start));
        Assert.That(regenerated[2].Start, Is.EqualTo(c.Start));

        for (int i = 0; i < regenerated.Length; i++)
        {
            Assert.That(regenerated[i].Id, Is.Not.EqualTo(originalIds[i]));
        }
        var idSet = new HashSet<Guid>(regenerated.Select(r => r.Id));
        Assert.That(idSet.Count, Is.EqualTo(3));
    }

    // The server answers an idempotency key it has already seen with the earlier job, so an AI node in a copy needs a
    // key seed of its own: otherwise Regenerate on the copy gets the original's picture or clip back.
    [Test]
    public void Regenerate_GivesTheAiNodesOfACopyRequestKeysOfTheirOwn()
    {
        var (element, image, nested) = CreateElementWithAiNodes();

        ObjectRegenerator.Regenerate(element, out Element copy);

        var (copiedImage, copiedNested) = FindAiNodes(copy);
        Assert.Multiple(() =>
        {
            Assert.That(copiedImage.RequestKeySeed, Is.Not.EqualTo(image.RequestKeySeed));
            Assert.That(copiedNested.RequestKeySeed, Is.Not.EqualTo(nested.RequestKeySeed), "a node inside a group");
            Assert.That(copiedImage.RequestKeySeed, Is.Not.Empty);
            Assert.That(copiedImage.Generations, Has.Count.EqualTo(1), "a copy still shows the result it was made from");
        });
    }

    // Duplicating and pasting several clips at once
    [Test]
    public void Regenerate_Array_GivesTheAiNodesOfEveryCopyRequestKeysOfTheirOwn()
    {
        var first = CreateElementWithAiNodes();
        var second = CreateElementWithAiNodes();
        // As two clips duplicated before copies got keys of their own.
        second.Image.RequestKeySeed = first.Image.RequestKeySeed;

        ObjectRegenerator.Regenerate([first.Element, second.Element], out Element[] copies);

        string[] seeds = copies.Select(FindAiNodes)
            .SelectMany(nodes => new[] { nodes.Image.RequestKeySeed, nodes.Nested.RequestKeySeed })
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(seeds, Is.Unique);
            Assert.That(seeds, Has.None.EqualTo(first.Image.RequestKeySeed));
            Assert.That(seeds, Has.None.EqualTo(first.Nested.RequestKeySeed));
            Assert.That(seeds, Has.None.EqualTo(second.Nested.RequestKeySeed));
        });
    }

    // Pasting a node graph filter effect in the property editor goes through the overload that takes a type
    [Test]
    public void Regenerate_ByType_GivesTheAiNodesOfACopyRequestKeysOfTheirOwn()
    {
        var effect = new NodeGraphFilterEffect();
        var image = new AiImageGenerationNode();
        effect.Model.CurrentValue!.Nodes.Add(image);

        ObjectRegenerator.Regenerate(effect, typeof(NodeGraphFilterEffect), out ICoreSerializable copy);

        AiImageGenerationNode copiedImage = ((NodeGraphFilterEffect)copy).Model.CurrentValue!.Nodes
            .OfType<AiImageGenerationNode>().Single();
        Assert.That(copiedImage.RequestKeySeed, Is.Not.EqualTo(image.RequestKeySeed));
    }

    [Test]
    public void Split_GivesTheAiNodesOfTheBackHalfRequestKeysOfTheirOwn()
    {
        using var harness = new SceneHistoryHarness("beutl_regenerate_split", duration: TimeSpan.FromSeconds(60));
        Element front = harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(10));
        var (_, image, nested) = AddAiNodes(front);
        harness.History.Commit("Setup");
        Element[] childrenBefore = [.. harness.Scene.Children];

        new ElementStructureService(harness.History)
            .Split(harness.Scene, [front], TimeSpan.FromSeconds(4));

        var (backImage, backNested) = FindAiNodes(harness.Scene.Children.Except(childrenBefore).Single());
        Assert.Multiple(() =>
        {
            Assert.That(backImage.RequestKeySeed, Is.Not.EqualTo(image.RequestKeySeed));
            Assert.That(backNested.RequestKeySeed, Is.Not.EqualTo(nested.RequestKeySeed));
        });
    }

    // A template saved by an earlier build still holds the seed of the node it was saved from
    [Test]
    public void ElementTemplate_GivesTheAiNodesOfEveryInstanceRequestKeysOfTheirOwn()
    {
        var (element, image, nested) = CreateElementWithAiNodes();
        (Type baseType, string format) = ObjectTemplateCategoryResolver.Resolve(typeof(Element));
        var template = new ObjectTemplateItem(
            Guid.NewGuid(), baseType, typeof(Element), CoreSerializer.SerializeToJsonObject(element), "AI clip", format,
            null);

        string[] seeds = Enumerable.Range(0, 2)
            .Select(_ => ElementTemplateResolver.CreateDescription(template, TimeSpan.Zero, 0).Source)
            .Cast<ElementSource.ElementTemplate>()
            .Select(source => FindAiNodes(source.Factory()))
            .SelectMany(nodes => new[] { nodes.Image.RequestKeySeed, nodes.Nested.RequestKeySeed })
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(seeds, Is.Unique);
            Assert.That(seeds, Has.None.EqualTo(image.RequestKeySeed));
            Assert.That(seeds, Has.None.EqualTo(nested.RequestKeySeed));
        });
    }

    // As a group node template does, an element template keeps the workflow and leaves one project's results behind
    [Test]
    public void SavingAnElementTemplate_LeavesTheAiResultsAndRequestKeysBehind()
    {
        var (element, image, _) = CreateElementWithAiNodes();
        image.ActiveGenerationId = image.Generations[0].Id;

        ObjectTemplateItem template = ObjectTemplateItem.CreateFromInstance(element, "AI clip");

        string json = template.Json.ToJsonString();
        var (instanceImage, _) = FindAiNodes((Element)template.CreateInstance()!);
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain($"\"{nameof(GenerativeNode.Generations)}\""));
            Assert.That(json, Does.Not.Contain($"\"{nameof(GenerativeNode.ActiveGenerationId)}\""));
            Assert.That(json, Does.Not.Contain($"\"{nameof(GenerativeNode.RequestKeySeed)}\""));
            Assert.That(json, Does.Not.Contain(image.RequestKeySeed));
            Assert.That(instanceImage.Generations, Is.Empty);
            Assert.That(instanceImage.RequestKeySeed, Is.Not.EqualTo(image.RequestKeySeed));
        });
    }

    // Only AI nodes leave their results behind: any other object may have members of the same names
    [Test]
    public void SavingATemplate_KeepsMembersNamedLikeAiResultsOnOtherObjects()
    {
        var (element, _, _) = CreateElementWithAiNodes();
        var other = new ObjectWithMembersNamedLikeAiResults();
        other.Generations.CurrentValue = 3;
        other.ActiveGenerationId.CurrentValue = "active";
        other.RequestKeySeed.CurrentValue = "seed";
        element.AddObject(other);

        ObjectTemplateItem template = ObjectTemplateItem.CreateFromInstance(element, "AI clip");

        var instance = (Element)template.CreateInstance()!;
        ObjectWithMembersNamedLikeAiResults restored =
            instance.Objects.OfType<ObjectWithMembersNamedLikeAiResults>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(restored.Generations.CurrentValue, Is.EqualTo(3));
            Assert.That(restored.ActiveGenerationId.CurrentValue, Is.EqualTo("active"));
            Assert.That(restored.RequestKeySeed.CurrentValue, Is.EqualTo("seed"));
            Assert.That(FindAiNodes(instance).Image.Generations, Is.Empty, "the AI node still leaves its results behind");
        });
    }

    // A template saved by an earlier build still holds the seed of the node it was saved from, and the property
    // editors apply what CreateInstance returns without regenerating it
    [Test]
    public void LegacyObjectTemplate_GivesTheAiNodesOfEveryInstanceRequestKeysOfTheirOwn()
    {
        var effect = new NodeGraphFilterEffect();
        var image = new AiImageGenerationNode();
        effect.Model.CurrentValue!.Nodes.Add(image);
        (Type baseType, string format) = ObjectTemplateCategoryResolver.Resolve(typeof(NodeGraphFilterEffect));
        var template = new ObjectTemplateItem(
            Guid.NewGuid(), baseType, typeof(NodeGraphFilterEffect), CoreSerializer.SerializeToJsonObject(effect),
            "AI effect", format, null);
        Assert.That(template.Json.ToJsonString(), Does.Contain(image.RequestKeySeed));

        string[] seeds = Enumerable.Range(0, 2)
            .Select(_ => ((NodeGraphFilterEffect)template.CreateInstance()!).Model.CurrentValue!.Nodes
                .OfType<AiImageGenerationNode>().Single().RequestKeySeed)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(seeds, Is.Unique);
            Assert.That(seeds, Has.None.EqualTo(image.RequestKeySeed));
        });
    }

    // A clip showing an AI node's result, with a second AI node inside a group node.
    private static (Element Element, AiImageGenerationNode Image, AiImageGenerationNode Nested) CreateElementWithAiNodes()
    {
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(5),
            Uri = new Uri(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.belm")),
        };
        var (_, image, nested) = AddAiNodes(element);
        return (element, image, nested);
    }

    private static (NodeGraphDrawable Drawable, AiImageGenerationNode Image, AiImageGenerationNode Nested) AddAiNodes(
        Element element)
    {
        var drawable = new NodeGraphDrawable();
        GraphModel graph = drawable.Model.CurrentValue!;
        var image = new AiImageGenerationNode();
        image.Generations.Add(new GenerationRecord { Summary = "kept result" });
        var group = new GroupNode();
        var nested = new AiImageGenerationNode();
        group.Group.Nodes.Add(nested);
        graph.Nodes.Add(image);
        graph.Nodes.Add(group);
        element.AddObject(drawable);
        return (drawable, image, nested);
    }

    private static (AiImageGenerationNode Image, AiImageGenerationNode Nested) FindAiNodes(Element element)
    {
        GraphModel graph = element.Objects.OfType<NodeGraphDrawable>().Single().Model.CurrentValue!;
        return (
            graph.Nodes.OfType<AiImageGenerationNode>().Single(),
            graph.Nodes.OfType<GroupNode>().Single().Group.Nodes.OfType<AiImageGenerationNode>().Single());
    }
}

// Not an AI node, but with members named like an AI node's results.
[SuppressResourceClassGeneration]
public sealed class ObjectWithMembersNamedLikeAiResults : EngineObject
{
    public ObjectWithMembersNamedLikeAiResults()
    {
        ScanProperties<ObjectWithMembersNamedLikeAiResults>();
    }

    public IExpressionProperty<int> Generations { get; } = Property.Create(0);

    public IExpressionProperty<string> ActiveGenerationId { get; } = Property.Create(string.Empty);

    public IExpressionProperty<string> RequestKeySeed { get; } = Property.Create(string.Empty);
}
