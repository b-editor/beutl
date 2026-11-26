using System.Text.Json;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.NodeGraphs;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.Language;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Shaders;
using Beutl.NodeGraph.Nodes.Utilities;

namespace Beutl.AgentToolkit.Tests.NodeGraphs;

public sealed class NodeGraphCatalogTests
{
    private static readonly NodeGraphTools s_tools = new(new AgentSessionManager());

    [OneTimeSetUp]
    public void RegisterNodes() => NodeTypeCatalog.EnsureBuiltInNodesRegistered();

    [Test]
    public void Registering_twice_adds_no_node_twice()
    {
        int count = NodeTypeCatalog.GetEntries().Count;

        NodeTypeCatalog.EnsureBuiltInNodesRegistered();

        Assert.That(NodeTypeCatalog.GetEntries(), Has.Count.EqualTo(count));
    }

    [Test]
    public void Without_filters_it_lists_names_only_and_stays_short()
    {
        ToolResult<ListNodeTypesResponse> result = s_tools.ListNodeTypes();

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.NodeTypes.Select(type => type.Type), Does.Contain(nameof(ShaderFullscreenPassNode)).And.Contain("FilterEffectNode<Blur>"));
            Assert.That(result.Value.NodeTypes.All(type => type.Members is null && type.Discriminator is null), Is.True);
            Assert.That(JsonSerializer.Serialize(result, JsonSerializerOptions.Web).Length, Is.LessThan(28_000));
        });
    }

    [Test]
    public void Categories_follow_the_add_node_menu_and_can_filter_effect_sections()
    {
        var result = s_tools.ListNodeTypes().Value!;
        var categories = result.NodeTypes.ToDictionary(node => node.Type, node => node.Category);
        string blur = $"{GraphicsStrings.FilterEffect}/{GraphicsStrings.FilterEffectCategory_BlurAndSharpen}";
        string scripts = $"{GraphicsStrings.FilterEffect}/{GraphicsStrings.FilterEffectCategory_ScriptAndNodes}";
        string matrices = $"{NodeGraphStrings.ToolsAndValues}/{NodeGraphStrings.TransformMatrices}";
        var effects = s_tools.ListNodeTypes(category: blur, includeMembers: false).Value!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(categories[nameof(OutputNode)], Is.EqualTo(NodeGraphStrings.InputAndOutput));
            Assert.That(categories[nameof(GeometryShapeNode)], Is.EqualTo(NodeGraphStrings.Geometry));
            Assert.That(categories[nameof(TextNode)], Is.EqualTo(NodeGraphStrings.Drawing));
            Assert.That(categories[nameof(TransformNode)], Is.EqualTo(matrices));
            Assert.That(categories[nameof(PreviewNode)], Is.EqualTo($"{NodeGraphStrings.ToolsAndValues}/{NodeGraphStrings.Debugging}"));
            Assert.That(categories["FilterEffectNode<Blur>"], Is.EqualTo(blur));
            Assert.That(categories["FilterEffectNode<CSharpScriptEffect>"], Is.EqualTo(scripts));
            Assert.That(effects.NodeTypes.Select(node => node.Type), Is.EquivalentTo(new[]
            {
                "FilterEffectNode<Blur>", "FilterEffectNode<Sharpen>", "FilterEffectNode<RadialBlur>", "FilterEffectNode<Bokeh>"
            }));
        }
    }

    [Test]
    public void A_category_adds_members_and_the_ports_code_declares()
    {
        // Categories are the editor's menu names, in the UI language.
        string shaders = NodeTypeCatalog.GetEntries().Single(entry => entry.Type == typeof(ShaderFullscreenPassNode)).Category;

        ToolResult<ListNodeTypesResponse> result = s_tools.ListNodeTypes(category: shaders);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        NodeTypeSummary pass = result.Value!.NodeTypes.Single(type => type.Type == nameof(ShaderFullscreenPassNode));
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.NodeTypes.All(type => type.Category.StartsWith(shaders, StringComparison.Ordinal)), Is.True);
            Assert.That(result.Value.NodeTypes.Select(type => type.Type), Does.Contain(nameof(ShaderRepeatNode)), "A category takes in its subcategories.");
            Assert.That(pass.Members!.Select(member => (member.Name, member.Kind)),
                Is.SupersetOf(new[] { ("Code", "input"), ("Out.color", "output"), ("In.source", "input"), ("Padding", "input"), ("Error", "monitor") }));
            Assert.That(pass.Members!.Single(member => member.Name == "Format").EnumValues, Does.Contain("RGBA16Float"));
            Assert.That(pass.Members!.Single(member => member.Name == "Format").Default!.GetValue<string>(), Is.EqualTo("RGBA16Float"));
            Assert.That(pass.Notes, Is.Not.Empty);
        });
    }

    [Test]
    public void A_type_list_names_what_it_did_not_find()
    {
        ToolResult<ListNodeTypesResponse> found = s_tools.ListNodeTypes(type: "FilterEffectNode<Blur>, NoSuchNode");
        ToolResult<ListNodeTypesResponse> none = s_tools.ListNodeTypes(type: "NoSuchNode");

        Assert.Multiple(() =>
        {
            Assert.That(found.Value!.NodeTypes.Single().Type, Is.EqualTo("FilterEffectNode<Blur>"));
            Assert.That(found.Value.UnmatchedTypes, Is.EqualTo(new[] { "NoSuchNode" }));
            Assert.That(none.Error?.Code, Is.EqualTo(ErrorCode.UnknownType));
        });
    }

    [Test]
    public void The_guide_names_the_types_it_documents_and_stays_short()
    {
        NodeGraphGuideResponse guide = s_tools.GetNodeGraphGuide().Value!;

        Assert.Multiple(() =>
        {
            Assert.That(guide.Guide, Does.Contain(IdentityHelper.WriteDiscriminator(typeof(NodeGraphFilterEffect))));
            Assert.That(guide.Guide, Does.Contain(IdentityHelper.WriteDiscriminator(typeof(NodeGraphDrawable))));
            Assert.That(guide.Guide, Does.Not.Contain("{NodeGraph"));
            Assert.That(JsonSerializer.Serialize(guide, JsonSerializerOptions.Web).Length, Is.LessThan(24_000));
            Assert.That(guide.Examples.Select(example => example.Name),
                Is.EquivalentTo(new[] { "animated-tint", "multi-pass-glow", "repeated-soften", "time-driven-wave" }));
        });
    }

    public static IEnumerable<string> ExampleNames() => NodeGraphGuide.LoadExamples().Select(example => example.Name);

    // The examples are what an agent copies first, so each one must apply to a new effect as it stands.
    [TestCaseSource(nameof(ExampleNames))]
    public void Each_example_applies_to_a_new_effect_without_errors(string name)
    {
        using var scene = new NodeGraphTestScene();
        NodeGraphExample example = NodeGraphGuide.LoadExamples().Single(candidate => candidate.Name == name);
        NodeGraphOperation[] operations = example.Operations.Deserialize<NodeGraphOperation[]>(JsonSerializerOptions.Web)!;

        ToolResult<EditNodeGraphResponse> result = scene.Edit(operations);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Applied, Is.True, string.Join("; ", result.Value.Diagnostics.Select(d => d.Message)));
            Assert.That(result.Value.Diagnostics, Is.Empty);
            Assert.That(result.Value.Created, Has.Count.EqualTo(operations.Count(operation => operation.Op == "add")));
        });
    }
}
