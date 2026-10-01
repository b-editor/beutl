using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Logging;
using Beutl.NodeGraph.Generative;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.NodeGraph.Nodes.Group;

/// <summary>A saved group node, listed by the name it was saved under.</summary>
public sealed record GroupNodeTemplate(string Name, string FilePath);

/// <summary>
/// Saves group nodes as reusable templates and inserts copies of them. A template is the
/// workflow, not its results: generation history and idempotency keys are left out. Identifiers are
/// renewed when a copy is added, so a template can be added any number of times.
/// </summary>
public sealed class GroupNodeTemplates(string directory)
{
    private static readonly ILogger s_logger = Log.CreateLogger<GroupNodeTemplates>();

    // Members of a generative node that belong to one project's results, not to the workflow.
    private static readonly string[] s_resultMembers = ["Generations", "ActiveGenerationId", "RequestKeySeed"];

    public static GroupNodeTemplates Default { get; } =
        new(Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "nodeTemplates"));

    public string Directory { get; } = directory;

    public IReadOnlyList<GroupNodeTemplate> List()
    {
        if (!System.IO.Directory.Exists(Directory))
            return [];

        return System.IO.Directory.EnumerateFiles(Directory, "*.json")
            .Select(path => new GroupNodeTemplate(Path.GetFileNameWithoutExtension(path), path))
            .OrderBy(template => template.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>Saves the group under <paramref name="name"/>, made unique; null for a name that cannot be a file.</summary>
    public GroupNodeTemplate? Save(GroupNode group, string name)
    {
        ArgumentNullException.ThrowIfNull(group);
        name = name.Trim();
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;

        JsonObject json = CoreSerializer.SerializeToJsonObject(group);
        StripResults(json);
        System.IO.Directory.CreateDirectory(Directory);
        string path = UniquePath(name);
        File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return new GroupNodeTemplate(Path.GetFileNameWithoutExtension(path), path);
    }

    /// <summary>
    /// The group as it was saved, identifiers included. Adding it to a graph takes a copy with
    /// identifiers of its own, made the way elements are duplicated, then <see cref="RenewRequestKeys"/>.
    /// </summary>
    public GroupNode? Load(GroupNodeTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        try
        {
            // The files are the user's to edit: a root that is not an object is a load failure.
            if (JsonNode.Parse(File.ReadAllText(template.FilePath)) is not JsonObject json)
                throw new JsonException("A node template must be a JSON object.");
            return CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphNode)) as GroupNode;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            s_logger.LogWarning(ex, "Failed to load node template {Path}.", template.FilePath);
            return null;
        }
    }

    /// <summary>Gives every AI node in the group a key of its own, so no copy shares a request.</summary>
    public static void RenewRequestKeys(GroupNode group)
    {
        ArgumentNullException.ThrowIfNull(group);
        foreach (GenerativeNode node in EnumerateNodes(group.Group).OfType<GenerativeNode>())
            node.RenewRequestKey();
    }

    private static IEnumerable<GraphNode> EnumerateNodes(GraphModel model)
    {
        foreach (GraphNode node in model.Nodes)
        {
            yield return node;
            if (node is GroupNode inner)
            {
                foreach (GraphNode nested in EnumerateNodes(inner.Group))
                    yield return nested;
            }
        }
    }

    private static void StripResults(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (string member in s_resultMembers)
                    obj.Remove(member);
                foreach (KeyValuePair<string, JsonNode?> child in obj.ToArray())
                    StripResults(child.Value);
                break;
            case JsonArray array:
                foreach (JsonNode? item in array)
                    StripResults(item);
                break;
        }
    }

    private string UniquePath(string name)
    {
        string path = Path.Combine(Directory, $"{name}.json");
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(Directory, $"{name} ({i}).json");
        return path;
    }
}
