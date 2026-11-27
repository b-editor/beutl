using System.ComponentModel;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record ObjectTemplateSummary(string TemplateId, string Name, string BaseType, string Type);
public sealed record ListTemplatesResponse(IReadOnlyList<ObjectTemplateSummary> Templates);
public sealed record ApplyObjectTemplateResponse(string ObjectId);

public sealed partial class ElementTemplateTools
{
    [McpServerTool(Name = "list_templates", ReadOnly = true)]
    [Description("Lists templates of all types in the Beutl profile and returns their IDs, base types, and concrete types.")]
    public ToolResult<ListTemplatesResponse> ListTemplates(
        [Description("Optional template base type, such as Brush or the baseType discriminator returned by this tool.")]
        string? baseType = null)
        => Execute(() => new ListTemplatesResponse(templates.GetTemplates()
            .Where(template => baseType is null
                || string.Equals(template.BaseType.Name, baseType, StringComparison.OrdinalIgnoreCase)
                || IdentityHelper.WriteDiscriminator(template.BaseType) == baseType)
            .Select(SummarizeObject)
            .ToArray()));

    [McpServerTool(Name = "save_object_template")]
    [Description("Saves the object with the specified Id in the scene document as a template in the Beutl profile.")]
    public ValueTask<ToolResult<ObjectTemplateSummary>> SaveObjectTemplate(
        [Description("Id of the object to save, including a nested object from read_document.")]
        string objectId,
        [Description("Template name. Beutl adds a numeric suffix when the name already exists.")]
        string name,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            Guid id = ReadId(objectId, nameof(objectId));
            string templateName = ReadTemplateName(name);
            IEditingSession session = sessions.RequireSession();
            ObjectTemplateItem? saved = await SaveTemplateAsync(session, templateName, ResolveContent, cancellationToken)
                .ConfigureAwait(false);
            return saved is not null
                ? SummarizeObject(saved)
                : throw Invalid(Strings.TemplateObjectSaveFailed, nameof(name));

            ICoreSerializable ResolveContent(Scene scene)
            {
                var (_, _, source) = ReadSceneObject(session, scene, id);
                return source switch
                {
                    IFallback => throw Invalid(Strings.TemplateObjectSaveFailed, objectId),
                    Element { IsRecoveryProtected: true } => throw Invalid(MessageStrings.RecoveryProtectedElementToolTip, objectId),
                    _ => source
                };
            }
        });

    [McpServerTool(Name = "apply_object_template")]
    [Description("Applies a template to a property in the scene document with the same locks as apply_edit. Replaces an object value or inserts a new item into an object list. Returns the new object's Id. The edit is one undo step.")]
    public ToolResult<ApplyObjectTemplateResponse> ApplyObjectTemplate(
        [Description("Template ID from list_templates or save_object_template.")]
        string templateId,
        [Description("Id of the object that owns the property, from read_document.")]
        string objectId,
        [Description("Exact PascalCase property name in read_document, such as Fill or Children.")]
        string propertyName,
        [Description("Zero-based insertion index for a list. Use the list length to append. Omit to replace an object-valued property.")]
        int? index = null,
        CancellationToken cancellationToken = default)
        => Execute(() =>
        {
            Guid id = ReadId(objectId, nameof(objectId));
            ObjectTemplateItem template = templates.FindById(ReadId(templateId, nameof(templateId)))
                ?? throw Stale(string.Format(Strings.TemplateNotFound, templateId), templateId);
            IEditingSession session = sessions.RequireSession();
            string createdId = string.Empty;
            new Reconciler(session.Fonts).ApplyFromCurrent(session, () =>
            {
                var (document, ownerJson, owner) = ReadSceneObject(session, RequireSceneRoot(session), id);
                (Type valueType, JsonArray? list) = ResolveProperty(owner, ownerJson, propertyName, index);
                if (!valueType.IsAssignableFrom(template.ActualType))
                    throw Invalid(string.Format(Strings.TemplateTypeMismatch, template.ActualType.Name, propertyName, valueType.Name), propertyName);
                if (template.CreateInstance() is not CoreObject instance || instance is IFallback
                    || !valueType.IsInstanceOfType(instance))
                    throw Invalid(string.Format(Strings.TemplateInstantiationFailed, template.Name.Value), templateId);

                // Renew all owned IDs and their internal references, as the timeline does for element templates.
                ObjectRegenerator.Regenerate(instance, out string regenerated);
                JsonObject value = JsonNode.Parse(regenerated)!.AsObject();
                createdId = value[nameof(CoreObject.Id)]!.GetValue<string>();
                HashSet<Guid> createdIds = CollectionReconciler.CollectIds(value);
                if (index is { } position)
                {
                    if (owner is Element && propertyName == nameof(Element.Objects)
                        && instance is EngineObject engineObject && Element.CreatePairedPortal(engineObject) is { } portal)
                    {
                        list!.Insert(position, CoreSerializer.SerializeToJsonObject(portal));
                        createdIds.Add(portal.Id.Value);
                        position++;
                    }

                    list!.Insert(position, value);
                }
                else
                {
                    ownerJson[propertyName] = value;
                }

                return (document, createdIds);
            }, CommandNames.ApplyTemplate, cancellationToken);

            return new ApplyObjectTemplateResponse(createdId);
        });

    private static (JsonObject Document, JsonObject Node, CoreObject Instance) ReadSceneObject(
        IEditingSession session, Scene scene, Guid id)
    {
        using var serialized = new SerializedObjectCapture();
        JsonObject document = session.Documents.Read(scene);
        CoreObject[] objects = serialized.Objects.Where(obj => obj.Id.Value == id).Take(2).ToArray();
        JsonObject[] nodes = CollectionReconciler.EnumerateObjectsWithIds(document)
            .Where(entry => entry.Id == id).Select(entry => entry.Object).Take(2).ToArray();
        if (objects.Length > 1 || nodes.Length > 1)
            throw Invalid(string.Format(Strings.TemplateObjectAmbiguous, id), id.ToString());
        if (objects.Length == 0 || nodes.Length == 0)
            throw Stale(string.Format(Strings.TemplateObjectNotFound, id), id.ToString());

        return (document, nodes[0], objects[0]);
    }

    private static (Type ValueType, JsonArray? List) ResolveProperty(
        CoreObject owner, JsonObject ownerJson, string propertyName, int? index)
    {
        IProperty? property = owner is Scene scene && propertyName == "Elements"
            ? scene.Children
            : owner.Properties.FirstOrDefault(property => property.Name == propertyName);
        if (property is not null && owner is not IFallback && property is not IExpressionProperty { IsReference: true }
            && (CoreObject.IsAutoSerialized(property) || ownerJson.ContainsKey(propertyName)))
        {
            if (index is { } position && property is IListProperty list && ownerJson[propertyName] is JsonArray items)
            {
                if (position < 0 || position > items.Count)
                    throw Invalid(string.Format(Strings.TemplateIndexOutOfRange, items.Count, propertyName), nameof(index));

                return (list.ElementType, items);
            }

            if (index is null && property is not IListProperty
                && (typeof(ICoreSerializable).IsAssignableFrom(property.ValueType) || property.Value is ICoreSerializable))
                return (property.ValueType, null);
        }

        throw Invalid(string.Format(Strings.TemplateInvalidProperty, propertyName), nameof(propertyName));
    }

    private static ObjectTemplateSummary SummarizeObject(ObjectTemplateItem template)
        => new(template.Id.ToString(), template.Name.Value,
            IdentityHelper.WriteDiscriminator(template.BaseType), IdentityHelper.WriteDiscriminator(template.ActualType));

    private static ReconcileException Stale(string message, string target)
        => new(new ToolError(ErrorCode.StaleHandle, message, target));
}
