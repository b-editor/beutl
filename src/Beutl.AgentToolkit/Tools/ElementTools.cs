using System.Collections.Immutable;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Documents;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Workspace;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

[McpServerToolType]
public sealed class ElementTools(AgentSessionManager sessions) : ToolBase
{
    private readonly Reconciler _reconciler = new();

    [McpServerTool(Name = "add_element")]
    [Description("Adds one timeline element through the declarative reconcile path.")]
    public ToolResult<ReconcileResult> AddElement(
        double startSeconds,
        double durationSeconds,
        int zIndex,
        string contentKind,
        string? mediaPath = null,
        string? text = null,
        string? shape = null)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            if (session.Root is not Scene)
            {
                throw new InvalidOperationException("The current session root is not a Scene.");
            }

            var element = new Element
            {
                Start = TimeSpan.FromSeconds(startSeconds),
                Length = TimeSpan.FromSeconds(durationSeconds),
                ZIndex = zIndex
            };

            EngineObject content = ContentFactory.Create(new ContentRequest(contentKind, mediaPath, text, shape));
            element.AddObject(content);

            JsonObject desired = session.Documents.Read(session.Root);
            JsonArray elements = desired["Elements"] as JsonArray
                                 ?? throw new InvalidOperationException("The current scene document does not contain an Elements array.");
            JsonObject elementJson = CoreSerializer.SerializeToJsonObject(element);
            RemoveIds(elementJson);
            elements.Add(elementJson);
            return _reconciler.Apply(session, desired);
        });
    }

    [McpServerTool(Name = "move_element")]
    [Description("Moves, retimes, or resizes a timeline element by Id.")]
    public ToolResult<ReconcileResult> MoveElement(
        string elementId,
        double? startSeconds = null,
        double? durationSeconds = null,
        int? zIndex = null)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            JsonObject desired = session.Documents.Read(session.Root);
            JsonObject element = FindElement(desired, elementId);
            if (startSeconds is { } start)
            {
                element[nameof(Element.Start)] = TimeSpan.FromSeconds(start).ToString("c");
            }

            if (durationSeconds is { } duration)
            {
                element[nameof(Element.Length)] = TimeSpan.FromSeconds(duration).ToString("c");
            }

            if (zIndex is { } index)
            {
                element[nameof(Element.ZIndex)] = index;
            }

            return _reconciler.Apply(session, desired);
        });
    }

    [McpServerTool(Name = "remove_element")]
    [Description("Deletes a timeline element by Id. Requires confirmDelete=true.")]
    public ToolResult<ReconcileResult> RemoveElement(string elementId, bool confirmDelete = false)
    {
        return Execute(() =>
        {
            if (!confirmDelete)
            {
                throw new DestructiveIntentException(elementId, "Deleting an element requires explicit confirmation.");
            }

            IEditingSession session = sessions.RequireSession();
            JsonObject desired = session.Documents.Read(session.Root);
            JsonArray elements = GetElements(desired);
            int index = IndexOf(elements, elementId);
            if (index < 0)
            {
                throw StaleElement(elementId);
            }

            JsonNode? removed = elements[index]?.DeepClone();
            elements.RemoveAt(index);
            ReconcileResult result = _reconciler.Apply(session, desired);
            return result with
            {
                Plan = result.Plan with
                {
                    Changes = [.. result.Plan.Changes, new ChangeSetEntry(ChangeOperations.RemoveChild, "$/Elements", elementId, removed)]
                }
            };
        });
    }

    [McpServerTool(Name = "duplicate_element")]
    [Description("Duplicates a timeline element by Id and optionally places the duplicate.")]
    public ToolResult<ReconcileResult> DuplicateElement(
        string elementId,
        double? startSeconds = null,
        int? zIndex = null)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            JsonObject desired = session.Documents.Read(session.Root);
            JsonArray elements = GetElements(desired);
            JsonObject source = FindElement(desired, elementId);
            JsonObject clone = (JsonObject)source.DeepClone();
            RemoveIds(clone);

            if (startSeconds is { } start)
            {
                clone[nameof(Element.Start)] = TimeSpan.FromSeconds(start).ToString("c");
            }

            if (zIndex is { } index)
            {
                clone[nameof(Element.ZIndex)] = index;
            }

            elements.Add(clone);
            return _reconciler.Apply(session, desired);
        });
    }

    [McpServerTool(Name = "split_element")]
    [Description("Splits a timeline element into two elements at an offset from its start.")]
    public ToolResult<ReconcileResult> SplitElement(string elementId, double splitOffsetSeconds)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            JsonObject desired = session.Documents.Read(session.Root);
            JsonArray elements = GetElements(desired);
            JsonObject element = FindElement(desired, elementId);
            TimeSpan start = ReadTime(element, nameof(Element.Start));
            TimeSpan length = ReadTime(element, nameof(Element.Length));
            TimeSpan split = TimeSpan.FromSeconds(splitOffsetSeconds);
            if (split <= TimeSpan.Zero || split >= length)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    "Split offset must be inside the element duration.",
                    elementId));
            }

            JsonObject clone = (JsonObject)element.DeepClone();
            RemoveIds(clone);
            element[nameof(Element.Length)] = split.ToString("c");
            clone[nameof(Element.Start)] = (start + split).ToString("c");
            clone[nameof(Element.Length)] = (length - split).ToString("c");
            elements.Add(clone);
            return _reconciler.Apply(session, desired);
        });
    }

    [McpServerTool(Name = "group_elements")]
    [Description("Adds a timeline group containing the supplied element Ids.")]
    public ToolResult<ReconcileResult> GroupElements(string[] elementIds)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            if (session.Root is not Scene scene)
            {
                throw new InvalidOperationException("The current session root is not a Scene.");
            }

            ImmutableHashSet<Guid> ids = ParseElementIds(scene, elementIds).ToImmutableHashSet();
            if (ids.Count < 2)
            {
                throw new ReconcileException(new ToolError(ErrorCode.ValidationRejected, "A group must contain at least two elements."));
            }

            session.History.ExecuteInTransaction(() => scene.Groups.Add(ids), "Agent group elements");
            return new ReconcileResult(
                new ReconcilePlan([new ChangeSetEntry("group-elements", "$/Groups", string.Join(",", ids))], []),
                session.Documents.Read(session.Root));
        });
    }

    [McpServerTool(Name = "ungroup_elements")]
    [Description("Removes timeline groups that contain any of the supplied element Ids.")]
    public ToolResult<ReconcileResult> UngroupElements(string[] elementIds)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            if (session.Root is not Scene scene)
            {
                throw new InvalidOperationException("The current session root is not a Scene.");
            }

            Guid[] ids = ParseElementIds(scene, elementIds).ToArray();
            session.History.ExecuteInTransaction(
                () =>
                {
                    for (int i = scene.Groups.Count - 1; i >= 0; i--)
                    {
                        if (scene.Groups[i].Overlaps(ids))
                        {
                            scene.Groups.RemoveAt(i);
                        }
                    }
                },
                "Agent ungroup elements");
            return new ReconcileResult(
                new ReconcilePlan([new ChangeSetEntry("ungroup-elements", "$/Groups", string.Join(",", ids))], []),
                session.Documents.Read(session.Root));
        });
    }

    private static JsonArray GetElements(JsonObject document)
    {
        return document["Elements"] as JsonArray
               ?? throw new InvalidOperationException("The current scene document does not contain an Elements array.");
    }

    private static JsonObject FindElement(JsonObject document, string elementId)
    {
        JsonArray elements = GetElements(document);
        int index = IndexOf(elements, elementId);
        if (index < 0)
        {
            throw StaleElement(elementId);
        }

        return (JsonObject)elements[index]!;
    }

    private static int IndexOf(JsonArray elements, string elementId)
    {
        for (int i = 0; i < elements.Count; i++)
        {
            if (elements[i] is JsonObject obj
                && obj.TryGetPropertyValue(nameof(CoreObject.Id), out JsonNode? idNode)
                && idNode?.GetValue<string>() == elementId)
            {
                return i;
            }
        }

        return -1;
    }

    private static void RemoveIds(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove(nameof(CoreObject.Id));
            foreach (JsonNode? child in obj.Select(pair => pair.Value).ToArray())
            {
                RemoveIds(child);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array.ToArray())
            {
                RemoveIds(child);
            }
        }
    }

    private static TimeSpan ReadTime(JsonObject obj, string propertyName)
    {
        return TimeSpan.Parse(obj[propertyName]!.GetValue<string>());
    }

    private static IEnumerable<Guid> ParseElementIds(Scene scene, IEnumerable<string> elementIds)
    {
        HashSet<Guid> existing = scene.Children.Select(element => element.Id).ToHashSet();
        foreach (string text in elementIds)
        {
            if (!Guid.TryParse(text, out Guid id) || !existing.Contains(id))
            {
                throw StaleElement(text);
            }

            yield return id;
        }
    }

    private static ReconcileException StaleElement(string elementId)
    {
        return new ReconcileException(new ToolError(
            ErrorCode.StaleHandle,
            $"No element with Id '{elementId}' exists in the current session.",
            elementId));
    }
}
