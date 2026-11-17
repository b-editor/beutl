using System.Reflection;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.NodeGraph;

namespace Beutl.AgentToolkit.NodeGraphs;

public sealed record NodeGraphExample(string Name, string Description, JsonArray Operations);

public sealed record NodeGraphGuideResponse(string SchemaVersion, string Guide, IReadOnlyList<NodeGraphExample> Examples);

// The contract of node graphs and shader passes, kept beside the code in Guide/ and embedded, with examples that the
// tests apply, so what agents read stays what the tools do.
internal static class NodeGraphGuide
{
    private const string GuideResource = "Beutl.AgentToolkit.NodeGraphs.Guide.node-graph-guide.md";
    private const string ExamplePrefix = "Beutl.AgentToolkit.NodeGraphs.Guide.examples.";

    private static readonly Lazy<string> s_guide = new(LoadGuide);

    public static NodeGraphGuideResponse Create()
        => new(SchemaVersion.Current, s_guide.Value, LoadExamples());

    public static IReadOnlyList<NodeGraphExample> LoadExamples()
    {
        Assembly assembly = typeof(NodeGraphGuide).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ExamplePrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name =>
            {
                using Stream stream = assembly.GetManifestResourceStream(name)!;
                JsonObject json = JsonNode.Parse(stream)!.AsObject();
                return new NodeGraphExample(
                    json["name"]!.GetValue<string>(),
                    json["description"]!.GetValue<string>(),
                    json["operations"]!.AsArray());
            })
            .ToArray();
    }

    private static string LoadGuide()
    {
        using Stream stream = typeof(NodeGraphGuide).Assembly.GetManifestResourceStream(GuideResource)
                              ?? throw new InvalidOperationException($"The resource '{GuideResource}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd()
            .Replace("{NodeGraphFilterEffect}", $"`$type` {IdentityHelper.WriteDiscriminator(typeof(NodeGraphFilterEffect))}", StringComparison.Ordinal)
            .Replace("{NodeGraphDrawable}", $"`$type` {IdentityHelper.WriteDiscriminator(typeof(NodeGraphDrawable))}", StringComparison.Ordinal);
    }
}
