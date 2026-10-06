using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed partial class EditTools
{
    [McpServerTool(Name = "duplicate_object")]
    [Description("Duplicates one EngineObject (e.g. a Drawable) within its owning timeline Element.Objects, minting fresh Ids on every nested node, and returns the new object's Id. The copy is appended after the original (front-most within the Element), so applying an additive-blend look to the returned objectId layers an emissive glow over the untouched original — see get_effect_recipe \"additive-bloom\". Pass wrapInGroup=true for additive bloom so the original and copy live under a DrawableGroup IFlowOperator and both drawables are processed together; duplicate then move the copy to a separate Element when you need independent timing or z-order.")]
    public ToolResult<DuplicateObjectResponse> DuplicateObject(
        [Description("Id of the object to duplicate. Must be an object inside some timeline Element.Objects (e.g. a drawable).")]
        string objectId,
        [Description("Optional owning Element Id to scope the search; omit to search every element for objectId.")]
        string? elementId = null,
        [Description("When true, wrap the original drawable and the new copy in a DrawableGroup inside the same Element. If the source drawable is already inside a DrawableGroup, the copy is appended to that existing group instead.")]
        bool wrapInGroup = false)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            if (wrapInGroup)
            {
                // Locate/read/clone the source and mutate all on the session dispatcher so a
                // LiveEditor wrap does not traverse the UI-owned scene on the MCP request thread.
                return session.ReadOnSession(() => DuplicateObjectIntoDrawableGroup(session, objectId, elementId));
            }

            // Read the source document, clone, and plan/apply all inside the reconcile dispatch so a
            // LiveEditor duplicate does not read the UI-owned scene on the MCP request thread.
            string elementId2 = string.Empty;
            var newId = Guid.NewGuid();
            ReconcileResult result = _reconciler.ApplyFromCurrent(session, desired =>
            {
                (JsonObject element, JsonArray objects, JsonObject source) = FindObjectInElements(desired, objectId, elementId);
                var clone = (JsonObject)source.DeepClone();
                RemoveIds(clone);
                clone[nameof(CoreObject.Id)] = newId.ToString();
                objects.Add(clone);
                elementId2 = ReadId(element) ?? string.Empty;
                return (desired, new HashSet<Guid> { newId });
            });

            return new DuplicateObjectResponse(
                result.Plan.Valid,
                elementId2,
                newId.ToString(),
                CreateCreatedIdSummary(result.Plan));
        });
    }

    private static DuplicateObjectResponse DuplicateObjectIntoDrawableGroup(
        IEditingSession session,
        string objectId,
        string? elementId)
    {
        if (session.Root is not Scene scene)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                "duplicate_object wrapInGroup requires a scene editing session.",
                objectId));
        }

        if (!Guid.TryParse(objectId, out Guid sourceId))
        {
            throw StaleObject(objectId);
        }

        Guid? scopedElementId = null;
        if (!string.IsNullOrWhiteSpace(elementId))
        {
            if (!Guid.TryParse(elementId, out Guid parsedElementId))
            {
                throw StaleObject(objectId);
            }

            scopedElementId = parsedElementId;
        }

        DuplicateObjectLocation location = FindObjectLocation(scene, sourceId, scopedElementId, objectId);
        if (location.Source is not Drawable source)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"Object '{objectId}' is not a Drawable and cannot be wrapped in a DrawableGroup.",
                objectId,
                "Use wrapInGroup only for drawable objects such as TextBlock, RectShape, SourceImage, or SourceVideo."));
        }

        if (location.ParentGroup is null && source is IFlowOperator)
        {
            // A flow operator placed directly on its element is preceded by its own paired portal.
            // Wrapping it moves the operator into a new group but leaves that portal behind, orphaned;
            // wrap one of its child drawables (or move it to another element) instead.
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"Object '{objectId}' is a flow operator on its element, so wrapping it in a new group would orphan its existing portal.",
                objectId,
                "Duplicate one of its child drawables with wrapInGroup, or move it into another element without wrapping."));
        }

        JsonObject sourceJson = FindObjectJsonInElements(session.Documents.Read(session.Root), objectId, elementId);
        var newId = Guid.NewGuid();
        Drawable clone = CloneDrawable(sourceJson, newId, session.Root);
        DrawableGroup group;
        PortalObject? portal = null;

        void Mutate()
        {
            if (location.ParentGroup is { } parentGroup)
            {
                group = parentGroup;
                parentGroup.Children.Add(clone);
                return;
            }

            int sourceIndex = location.Element.Objects.IndexOf(location.Source);
            if (sourceIndex < 0)
            {
                throw StaleObject(objectId);
            }

            group = new DrawableGroup
            {
                Name = string.IsNullOrWhiteSpace(source.Name)
                    ? "Drawable bloom group"
                    : $"{source.Name} bloom group"
            };

            location.Element.InsertObject(sourceIndex, group);
            portal = location.Element.Objects[sourceIndex] as PortalObject;
            location.Element.Objects.Remove(location.Source);
            group.Children.Add(source);
            group.Children.Add(clone);
        }

        group = null!;
        ExecuteInSessionTransaction(session, Mutate, "Agent duplicate object");

        var createdIds = new List<AppliedEntityId>();
        if (portal is not null)
        {
            createdIds.Add(CreateAppliedEntityId(
                portal,
                $"$/Elements[Id={location.Element.Id}]/Objects[Id={portal.Id}]"));
        }

        if (location.ParentGroup is null)
        {
            createdIds.Add(CreateAppliedEntityId(
                group,
                $"$/Elements[Id={location.Element.Id}]/Objects[Id={group.Id}]"));
        }

        createdIds.Add(CreateAppliedEntityId(
            clone,
            $"$/Elements[Id={location.Element.Id}]/Objects[Id={group.Id}]/Children[Id={clone.Id}]"));

        return new DuplicateObjectResponse(
            true,
            location.Element.Id.ToString(),
            clone.Id.ToString(),
            createdIds,
            group.Id.ToString());
    }

    private static void ExecuteInSessionTransaction(IEditingSession session, Action mutate, string name)
    {
        void Execute() => session.History.ExecuteInTransaction(mutate, name);

        if (session is IEditingSessionDispatcher dispatcher)
        {
            dispatcher.Invoke(Execute);
        }
        else
        {
            Execute();
        }

        if (session is FileEditingSession fileSession)
        {
            fileSession.MarkDirty();
        }
    }

    private static Drawable CloneDrawable(JsonObject sourceJson, Guid newId, CoreObject root)
    {
        var cloneJson = (JsonObject)sourceJson.DeepClone();
        RemoveIds(cloneJson);
        cloneJson[nameof(CoreObject.Id)] = newId.ToString();

        return (Drawable)CoreSerializer.DeserializeFromJsonObject(
            cloneJson,
            typeof(Drawable),
            new CoreSerializerOptions
            {
                BaseUri = root.Uri,
                Mode = CoreSerializationMode.Read | CoreSerializationMode.EmbedReferencedObjects
            });
    }

    private static AppliedEntityId CreateAppliedEntityId(CoreObject obj, string path)
    {
        return new AppliedEntityId(
            obj.Id.ToString(),
            path,
            IdentityHelper.WriteDiscriminator(obj.GetType()),
            string.IsNullOrWhiteSpace(obj.Name) ? null : obj.Name);
    }

    private static DuplicateObjectLocation FindObjectLocation(
        Scene scene,
        Guid objectId,
        Guid? elementId,
        string objectIdText)
    {
        foreach (Element element in scene.Children)
        {
            if (elementId is not null && element.Id != elementId)
            {
                continue;
            }

            foreach (EngineObject obj in element.Objects)
            {
                if (obj.Id == objectId)
                {
                    return new DuplicateObjectLocation(element, obj, null);
                }

                if (obj is DrawableGroup group
                    && TryFindObjectInDrawableGroup(element, group, objectId, out DuplicateObjectLocation? location))
                {
                    return location;
                }
            }
        }

        throw StaleObject(objectIdText);
    }

    private static bool TryFindObjectInDrawableGroup(
        Element element,
        DrawableGroup group,
        Guid objectId,
        [NotNullWhen(true)] out DuplicateObjectLocation? location)
    {
        foreach (Drawable child in group.Children)
        {
            if (child.Id == objectId)
            {
                location = new DuplicateObjectLocation(element, child, group);
                return true;
            }

            if (child is DrawableGroup childGroup
                && TryFindObjectInDrawableGroup(element, childGroup, objectId, out location))
            {
                return true;
            }
        }

        location = null;
        return false;
    }

    private static JsonObject FindObjectJsonInElements(
        JsonObject document,
        string objectId,
        string? elementId)
    {
        if (document["Elements"] is not JsonArray elements)
        {
            throw new InvalidOperationException("The current scene document does not contain an Elements array.");
        }

        foreach (JsonNode? elementNode in elements)
        {
            if (elementNode is not JsonObject element
                || (elementId is not null && ReadId(element) != elementId)
                || element["Objects"] is not JsonArray objects)
            {
                continue;
            }

            if (FindObjectJson(objects, objectId) is { } source)
            {
                return source;
            }
        }

        throw StaleObject(objectId);
    }

    private static JsonObject? FindObjectJson(JsonNode? node, string objectId)
    {
        if (node is JsonObject obj)
        {
            if (ReadId(obj) == objectId)
            {
                return obj;
            }

            foreach (JsonNode? child in obj.Select(pair => pair.Value).ToArray())
            {
                if (FindObjectJson(child, objectId) is { } found)
                {
                    return found;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array.ToArray())
            {
                if (FindObjectJson(child, objectId) is { } found)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static (JsonObject Element, JsonArray Objects, JsonObject Source) FindObjectInElements(
        JsonObject document,
        string objectId,
        string? elementId)
    {
        if (document["Elements"] is not JsonArray elements)
        {
            throw new InvalidOperationException("The current scene document does not contain an Elements array.");
        }

        foreach (JsonNode? elementNode in elements)
        {
            if (elementNode is not JsonObject element
                || (elementId is not null && ReadId(element) != elementId)
                || element["Objects"] is not JsonArray objects)
            {
                continue;
            }

            foreach (JsonNode? objectNode in objects)
            {
                if (objectNode is JsonObject source && ReadId(source) == objectId)
                {
                    return (element, objects, source);
                }
            }
        }

        throw new ReconcileException(new ToolError(
            ErrorCode.StaleHandle,
            $"No object with Id '{objectId}' exists in a timeline element.",
            objectId));
    }

    private static ReconcileException StaleObject(string objectId)
    {
        return new ReconcileException(new ToolError(
            ErrorCode.StaleHandle,
            $"No object with Id '{objectId}' exists in a timeline element.",
            objectId));
    }

    private static string? ReadId(JsonObject obj)
    {
        return obj.TryGetPropertyValue(nameof(CoreObject.Id), out JsonNode? node)
            ? node?.GetValue<string>()
            : null;
    }
}
