using System.ComponentModel;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.Configuration;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record ElementTemplateSummary(string TemplateId, string Name, bool IsElementSet);
public sealed record ListElementTemplatesResponse(IReadOnlyList<ElementTemplateSummary> Templates);
public sealed record AddElementFromTemplateResponse(IReadOnlyList<string> CreatedElementIds);

[McpServerToolType]
public sealed partial class ElementTemplateTools(
    AgentSessionManager sessions, ObjectTemplateService templates, GlobalConfiguration configuration) : ToolBase
{
    [McpServerTool(Name = "list_element_templates", ReadOnly = true)]
    [Description("Lists saved element and element-set templates in the Beutl profile and returns their IDs.")]
    public ToolResult<ListElementTemplatesResponse> ListElementTemplates()
        => Execute(() => new ListElementTemplatesResponse(
            ElementTemplateResolver.FindElementTemplates(templates).Select(Summarize).ToArray()));

    [McpServerTool(Name = "save_element_template")]
    [Description("Saves elements from the target scene to the Beutl profile's template library and returns the saved template. A set preserves relative timing, layers, and groups among the saved elements.")]
    public ValueTask<ToolResult<ElementTemplateSummary>> SaveElementTemplate(
        [Description("Template name. Beutl adds a numeric suffix when the name already exists.")]
        string name,
        [Description("Element IDs from read_document_summary. Omit only in a live editor to save its selected clips and their groups.")]
        string[]? elementIds = null,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            string templateName = ReadTemplateName(name);
            IEditingSession session = sessions.RequireSession();
            ObjectTemplateItem? saved = await SaveTemplateAsync(session, templateName, ResolveContent, cancellationToken)
                .ConfigureAwait(false);
            return saved is not null
                ? Summarize(saved)
                : throw Invalid("Beutl could not save the element template.", nameof(name));

            ICoreSerializable ResolveContent(Scene scene)
            {
                IReadOnlyList<Element> elements = elementIds is null && session is LiveEditingSession liveSession
                    ? liveSession.Elements.GetSelectedElements()
                    : ResolveElements(scene, elementIds);
                if (elements.Count == 0)
                    throw Invalid("Select at least one element in the target editor or pass elementIds.", nameof(elementIds));

                if (elements.FirstOrDefault(element => element.IsRecoveryProtected) is { } protectedElement)
                {
                    throw Invalid("Repair this recovery-protected element before saving it as a template.",
                        protectedElement.Id.Value.ToString());
                }

                return ElementTemplateResolver.CreateTemplateContent(scene, elements);
            }
        });

    [McpServerTool(Name = "add_element_from_template")]
    [Description("Adds a saved element or set at the requested timeline placement using Beutl's template placement rules. Returns the new element IDs. The insertion is one undo step.")]
    public ValueTask<ToolResult<AddElementFromTemplateResponse>> AddElementFromTemplate(
        [Description("Template ID from list_element_templates or save_element_template.")]
        string templateId,
        [Description("Placement in seconds from timeline zero, rounded to the nearest frame.")]
        double timeSeconds,
        [Description("Zero-based target layer. A set uses this as its lowest layer.")]
        int layer,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            Guid id = ReadId(templateId, nameof(templateId));
            ObjectTemplateItem[] matches = ElementTemplateResolver.FindElementTemplates(templates)
                .Where(template => template.Id == id).Take(2).ToArray();
            if (matches.Length == 0)
                throw new ReconcileException(new ToolError(ErrorCode.StaleHandle, "Element template was not found.", templateId));
            if (matches.Length > 1)
                throw Invalid("More than one element template has this ID.", templateId);
            if (layer < 0)
                throw Invalid("Supply a non-negative layer index.", nameof(layer));

            IEditingSession session = sessions.RequireSession();
            if (session is LiveEditingSession live)
            {
                var (targetScene, liveDescription) = live.ReadOnSession(() =>
                {
                    Scene scene = RequireSceneRoot(live);
                    return (scene, Describe(scene));
                });
                ElementAddResult result = await live.Elements.AddElementsAsync(
                    targetScene, [liveDescription], CaptureLockedTransitionValidation, cancellationToken).ConfigureAwait(false);
                if (result.Failure is { } failure)
                {
                    if (failure.Exception is ReconcileException protectedEdit) throw protectedEdit;
                    throw Invalid(failure.Message, failure is LockedElementLayerFailure ? nameof(layer) : templateId);
                }

                return ToResponse(result.Elements);
            }

            return session.EditOnSession(() =>
            {
                Scene scene = RequireSceneRoot(session);
                ElementDescription description = Describe(scene);
                IReadOnlyList<Element> added = [];
                session.History.ExecuteInTransaction(() =>
                {
                    Action? validate = CaptureLockedTransitionValidation(scene);
                    added = AddToFileScene(scene, description);
                    validate?.Invoke();
                }, CommandNames.AddElement, cancellationToken: cancellationToken);
                session.MarkDirtyIfFileSession();
                return ToResponse(added);
            }, cancellationToken);

            ElementDescription Describe(Scene scene) => ElementTemplateResolver.CreateDescription(
                matches[0], ReadFrameTime(scene, timeSeconds), layer);
        });

    private IReadOnlyList<Element> AddToFileScene(Scene scene, ElementDescription description)
    {
        if (scene.IsLayerLocked(description.Layer))
            throw Invalid($"Timeline layer {description.Layer} is locked.", "layer");

        bool autoAdjust = configuration.EditorConfig.AutoAdjustSceneDuration.Value;
        if (description.Source is ElementSource.ElementTemplate single)
        {
            Element element = single.Factory();
            element.Start.Value = description.Start;
            element.ZIndex.Value = description.Layer;
            SetElementUri(scene, element);
            scene.AddChild(element, ElementOverlapHandling.Auto, autoAdjust);
            return [element];
        }

        ElementSet set = ((ElementSource.ElementSetTemplate)description.Source).Factory();
        Element[] elements = [.. set.Elements];
        if (elements.Length == 0)
            throw Invalid("The element-set template holds no elements.", "templateId");

        GroupPlacement placement = ElementPlacementHelper.FindFreePlacement(scene, elements, description.Start, description.Layer)
            ?? throw Invalid("No free placement was found for the element-set template.", "layer");
        ElementPlacementHelper.MoveTogether(elements, placement.StartDelta, placement.LayerDelta);
        foreach (Element element in elements)
        {
            SetElementUri(scene, element);
            scene.AddChild(element, ElementOverlapHandling.Allow, autoAdjust);
        }
        scene.Groups.AddRange(set.Groups);
        return elements;
    }

    private static void SetElementUri(Scene scene, Element element)
    {
        if (scene.Uri is { } uri)
            element.Uri = ElementFileNaming.GetUri(uri, element.Id.Value);
    }

    private static Element[] ResolveElements(Scene scene, string[]? elementIds)
    {
        if (elementIds is not { Length: > 0 })
            throw Invalid("Supply at least one element ID from read_document_summary.", nameof(elementIds));

        var byId = scene.Children.ToLookup(element => element.Id.Value);
        return elementIds.Select(value => ReadId(value, nameof(elementIds))).Distinct().Select(id =>
        {
            Element[] matches = byId[id].Take(2).ToArray();
            return matches.Length switch
            {
                1 => matches[0],
                0 => throw new ReconcileException(new ToolError(ErrorCode.StaleHandle,
                    $"Element '{id}' was not found in the target scene.", id.ToString())),
                _ => throw Invalid($"More than one element has ID '{id}' in the target scene.", id.ToString())
            };
        }).ToArray();
    }

    private static MediaTime ReadFrameTime(Scene scene, double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            throw Invalid("Supply finite, non-negative seconds.", "timeSeconds");

        try
        {
            return MediaTime.FromSeconds(seconds).RoundToFrame(
                SceneTimeRangeService.GetFrameRate(scene), MidpointRounding.AwayFromZero);
        }
        catch (OverflowException)
        {
            throw Invalid("The time is outside the supported timeline range.", "timeSeconds");
        }
    }

    private async ValueTask<ObjectTemplateItem?> SaveTemplateAsync(
        IEditingSession session, string name, Func<Scene, ICoreSerializable> resolveContent, CancellationToken cancellationToken)
    {
        if (session is LiveEditingSession live)
        {
            ValueTask<ObjectTemplateItem?> pending = await live.ExecuteHistoryMutationAsync(
                static _ => true, history => !history.IsEditing ? Save() : throw new UserEditingException(),
                cancellationToken, waitForEditSession: true).ConfigureAwait(false);
            return await pending.ConfigureAwait(false);
        }

        return await session.ReadOnSession(Save).ConfigureAwait(false);

        ValueTask<ObjectTemplateItem?> Save()
        {
            cancellationToken.ThrowIfCancellationRequested();
            return templates.AddFromInstanceAsync(resolveContent(RequireSceneRoot(session)), name, cancellationToken, session.Fonts);
        }
    }

    private static string ReadTemplateName(string name)
    {
        string templateName = name?.Trim() ?? string.Empty;
        return templateName.Length != 0 && ObjectTemplateService.IsValidTemplateName(templateName)
            ? templateName
            : throw Invalid(Strings.TemplateInvalidName, nameof(name));
    }

    private static Guid ReadId(string value, string target)
        => Guid.TryParse(value, out Guid id) && id != Guid.Empty ? id : throw Invalid(Strings.TemplateInvalidId, target);

    private static ReconcileException Invalid(string message, string target)
        => new(new ToolError(ErrorCode.ValidationRejected, message, target));

    private static ElementTemplateSummary Summarize(ObjectTemplateItem template)
        => new(template.Id.ToString(), template.Name.Value, template.BaseType == typeof(ElementSet));

    private static AddElementFromTemplateResponse ToResponse(IEnumerable<Element> elements)
        => new(elements.Select(element => element.Id.Value.ToString()).ToArray());
}
