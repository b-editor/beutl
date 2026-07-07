using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
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

    // Element.Start/Length do not clamp, so an unvalidated negative time persists an element with a
    // negative range that later sampling reads before the timeline instead of rejecting the request.
    private static void ValidateStart(double startSeconds, string field)
    {
        if (!double.IsFinite(startSeconds) || startSeconds < 0)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"'{field}' must be a finite, non-negative number of seconds.",
                field));
        }
    }

    private static void ValidateDuration(double durationSeconds, string field)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"'{field}' must be a finite, positive number of seconds.",
                field));
        }
    }

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
            ValidateStart(startSeconds, nameof(startSeconds));
            ValidateDuration(durationSeconds, nameof(durationSeconds));
            IEditingSession session = sessions.RequireSession();
            return _reconciler.ApplyFromCurrent(session, current =>
            {
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

                JsonArray elements = current["Elements"] as JsonArray
                                     ?? throw new InvalidOperationException("The current scene document does not contain an Elements array.");
                JsonObject elementJson = CoreSerializer.SerializeToJsonObject(element);
                RemoveIds(elementJson);
                elements.Add(elementJson);
                return (current, null);
            });
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
            if (startSeconds is { } startValue)
            {
                ValidateStart(startValue, nameof(startSeconds));
            }

            if (durationSeconds is { } durationValue)
            {
                ValidateDuration(durationValue, nameof(durationSeconds));
            }

            IEditingSession session = sessions.RequireSession();
            return _reconciler.ApplyFromCurrent(session, current =>
            {
                JsonObject element = FindElement(current, elementId);
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

                return (current, null);
            });
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
            JsonNode? removed = null;
            ReconcileResult result = _reconciler.ApplyFromCurrent(session, current =>
            {
                JsonArray elements = GetElements(current);
                int index = IndexOf(elements, elementId);
                if (index < 0)
                {
                    throw StaleElement(elementId);
                }

                removed = elements[index]?.DeepClone();
                elements.RemoveAt(index);
                return (current, null);
            });
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
            if (startSeconds is { } startValue)
            {
                ValidateStart(startValue, nameof(startSeconds));
            }

            IEditingSession session = sessions.RequireSession();
            return _reconciler.ApplyFromCurrent(session, current =>
            {
                JsonArray elements = GetElements(current);
                JsonObject source = FindElement(current, elementId);
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
                return (current, null);
            });
        });
    }

    [McpServerTool(Name = "split_element")]
    [Description("Splits a timeline element into two elements at an offset from its start.")]
    public ToolResult<ReconcileResult> SplitElement(string elementId, double splitOffsetSeconds)
    {
        return Execute(() =>
        {
            ValidateDuration(splitOffsetSeconds, nameof(splitOffsetSeconds));
            IEditingSession session = sessions.RequireSession();
            return _reconciler.ApplyFromCurrent(session, current =>
            {
                JsonArray elements = GetElements(current);
                JsonObject element = FindElement(current, elementId);
                TimeSpan start = ReadTime(element, nameof(Element.Start));
                TimeSpan length = ReadTime(element, nameof(Element.Length));
                // Compare in seconds before converting: TimeSpan.FromSeconds overflows on huge values.
                if (splitOffsetSeconds >= length.TotalSeconds)
                {
                    throw new ReconcileException(new ToolError(
                        ErrorCode.ValidationRejected,
                        "Split offset must be inside the element duration.",
                        elementId));
                }

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
                return (current, null);
            });
        });
    }

    [McpServerTool(Name = "group_elements")]
    [Description("Adds a timeline group containing the supplied element Ids.")]
    public ToolResult<ReconcileResult> GroupElements(string[] elementIds)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            ImmutableHashSet<Guid> ids = [];
            JsonObject document = new();
            session.InvokeOnSession(() =>
            {
                if (session.Root is not Scene scene)
                {
                    throw new InvalidOperationException("The current session root is not a Scene.");
                }

                ids = ParseElementIds(scene, elementIds).ToImmutableHashSet();
                if (ids.Count < 2)
                {
                    throw new ReconcileException(new ToolError(ErrorCode.ValidationRejected, "A group must contain at least two elements."));
                }

                session.History.ExecuteInTransaction(() => scene.Groups.Add(ids), "Agent group elements");
                document = session.Documents.Read(session.Root);
            });
            MarkFileSessionDirty(session);
            return new ReconcileResult(
                new ReconcilePlan([new ChangeSetEntry("group-elements", "$/Groups", string.Join(",", ids))], []),
                document);
        });
    }

    [McpServerTool(Name = "ungroup_elements")]
    [Description("Removes timeline groups that contain any of the supplied element Ids.")]
    public ToolResult<ReconcileResult> UngroupElements(string[] elementIds)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            Guid[] ids = [];
            JsonObject document = new();
            session.InvokeOnSession(() =>
            {
                if (session.Root is not Scene scene)
                {
                    throw new InvalidOperationException("The current session root is not a Scene.");
                }

                ids = ParseElementIds(scene, elementIds).ToArray();
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
                document = session.Documents.Read(session.Root);
            });
            MarkFileSessionDirty(session);
            return new ReconcileResult(
                new ReconcilePlan([new ChangeSetEntry("ungroup-elements", "$/Groups", string.Join(",", ids))], []),
                document);
        });
    }

    // group/ungroup mutate history directly instead of through Reconciler.Apply, so the file session
    // would otherwise stay IsDirty:false and a client could skip save_project and lose the change.
    private static void MarkFileSessionDirty(IEditingSession session)
    {
        if (session is FileEditingSession fileSession)
        {
            fileSession.MarkDirty();
        }
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
        // Document time strings are always constant ("c") format; a culture-sensitive parse breaks
        // fractional values on comma-decimal locales.
        return TimeSpan.ParseExact(obj[propertyName]!.GetValue<string>(), "c", CultureInfo.InvariantCulture);
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
