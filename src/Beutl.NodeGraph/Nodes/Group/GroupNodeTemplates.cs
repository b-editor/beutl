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
/// workflow, not its results: generation history and idempotency keys are left out, and every
/// identifier is renewed on insert so a template can be added any number of times.
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

    /// <summary>A new group node built from the template, with identifiers of its own.</summary>
    public GroupNode? Instantiate(GroupNodeTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        try
        {
            string text = File.ReadAllText(template.FilePath);
            // The files are the user's to edit: a root that is not an object is a load failure.
            if (JsonNode.Parse(text) is not JsonObject json)
                throw new JsonException("A node template must be a JSON object.");
            text = RenewIdentifiers(json.ToJsonString());
            if (JsonNode.Parse(text) is not JsonObject renewed)
                throw new JsonException("A node template must be a JSON object.");
            if (CoreSerializer.DeserializeFromJsonObject(renewed, typeof(GraphNode)) is not GroupNode group)
                return null;

            foreach (GenerativeNode node in EnumerateNodes(group.Group).OfType<GenerativeNode>())
                node.RenewRequestKey();
            return group;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            s_logger.LogWarning(ex, "Failed to load node template {Path}.", template.FilePath);
            return null;
        }
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

    // Every object's "Id" is a GUID, and connections, references and bindings point at those
    // same strings. Replacing each wherever it appears keeps all of them pointing at the copy.
    private static string RenewIdentifiers(string text)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectIds(JsonNode.Parse(text), ids);
        foreach (string id in ids)
            text = text.Replace(id, Guid.NewGuid().ToString(), StringComparison.OrdinalIgnoreCase);
        return text;
    }

    private static void CollectIds(JsonNode? node, HashSet<string> ids)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.TryGetPropertyValue("Id", out JsonNode? id)
                    && id is JsonValue value
                    && value.TryGetValue(out string? text)
                    && Guid.TryParse(text, out Guid guid)
                    && guid != Guid.Empty)
                {
                    ids.Add(text);
                }

                foreach (KeyValuePair<string, JsonNode?> child in obj)
                    CollectIds(child.Value, ids);
                break;
            case JsonArray array:
                foreach (JsonNode? item in array)
                    CollectIds(item, ids);
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
