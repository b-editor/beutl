using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.LibraryTab;

// Adds library items and templates at the playhead, the way a drop on the player does, so neither the
// library nor the timeline has to be open.
public sealed partial class LibraryTabExtension : IContextCommandHandler
{
    private static readonly ILogger s_logger = Log.CreateLogger<LibraryTabExtension>();
    private static readonly TimeSpan s_defaultElementLength = TimeSpan.FromSeconds(5);

    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        new("AddElement", Strings.AddElement, "", []) { Scope = ContextCommandScope.Extension },
        new("AddElementFromTemplate", Strings.AddFromTemplate, "", []) { Scope = ContextCommandScope.Extension },
    ];

    public bool CanExecute(ContextCommandExecution execution)
    {
        if (execution.EditorContext?.GetService<Scene>() is null) return false;

        return execution.CommandName switch
        {
            "AddElement" => true,
            "AddElementFromTemplate" => FindElementTemplates().Length > 0,
            _ => false,
        };
    }

    public async Task ExecuteAsync(ContextCommandExecution execution)
    {
        if (execution.Interaction is not { } interaction
            || execution.EditorContext is not { } editorContext
            || editorContext.GetService<Scene>() is not { } scene)
        {
            return;
        }

        switch (execution.CommandName)
        {
            case "AddElement":
                {
                    ContextCommandPickItem<Type>[] items = EnumerateEngineObjects(LibraryService.Current.Items, null)
                        .ToArray();
                    if (await interaction.ShowQuickPickAsync(items) is not { Value: { } type }) return;

                    await AddAtPlayheadAsync(editorContext, scene, interaction.CancellationToken, (start, layer) => new ElementDescription(
                        start, s_defaultElementLength, layer,
                        new ElementSource.EngineObject(() => (EngineObject)Activator.CreateInstance(type)!)));
                    break;
                }

            case "AddElementFromTemplate":
                {
                    ContextCommandPickItem<ObjectTemplateItem>[] items = FindElementTemplates()
                        .Select(t => new ContextCommandPickItem<ObjectTemplateItem>(t.Name.Value, t))
                        .ToArray();
                    if (await interaction.ShowQuickPickAsync(items) is not { Value: { } template }) return;

                    await AddAtPlayheadAsync(editorContext, scene, interaction.CancellationToken,
                        (start, layer) => ElementTemplateResolver.CreateDescription(template, start, layer));
                    break;
                }
        }
    }

    private static ObjectTemplateItem[] FindElementTemplates()
    {
        return ObjectTemplateService.Instance.FindByBaseType(typeof(Element)).ToArray();
    }

    // Flattens the library the way the timeline's "Add element" menu does; the group path goes in the
    // description so typing a group name filters to its items.
    private static IEnumerable<ContextCommandPickItem<Type>> EnumerateEngineObjects(
        IEnumerable<LibraryItem> items, string? group)
    {
        foreach (LibraryItem item in items)
        {
            switch (item)
            {
                case SingleTypeLibraryItem single when single.Format == KnownLibraryItemFormats.EngineObject:
                    yield return new(single.DisplayName, single.ImplementationType, group);
                    break;

                case MultipleTypeLibraryItem multiple
                    when multiple.Types.TryGetValue(KnownLibraryItemFormats.EngineObject, out Type? type):
                    yield return new(multiple.DisplayName, type, group);
                    break;

                case GroupLibraryItem child:
                    string path = group is null ? child.DisplayName : $"{group} / {child.DisplayName}";
                    foreach (ContextCommandPickItem<Type> nested in EnumerateEngineObjects(child.Items, path))
                        yield return nested;
                    break;
            }
        }
    }

    // The cancellation token stops the add when the user switches editors while it is being prepared.
    private static async Task AddAtPlayheadAsync(
        IEditorContext editorContext, Scene scene, CancellationToken cancellationToken,
        Func<TimeSpan, int, ElementDescription> createDescription)
    {
        TimeSpan time = editorContext.GetService<IEditorClock>()?.CurrentTime.Value ?? TimeSpan.Zero;
        TimeSpan start = time.RoundToRate(scene.FindHierarchicalParent<Project>().GetFrameRate());
        // Above everything showing at the playhead, like a drop on the player.
        int layer = scene.Children
            .Where(e => e.Start <= start && start < e.Range.End)
            .Select(e => e.ZIndex + 1)
            .DefaultIfEmpty(0)
            .Max();
        ElementDescription description = createDescription(start, layer);

        // An open timeline also scrolls to the new clip and reports the failure itself.
        if (editorContext.FindToolTab<TimelineTabViewModel>() is { } timeline)
        {
            await timeline.AddElementWithResultAsync(description, cancellationToken);
            return;
        }

        ElementAddResult result = await editorContext.GetRequiredService<IElementAdder>()
            .AddAsync([description], cancellationToken);
        if (result.IsSuccess) return;

        if (result.Failure is LockedElementLayerFailure)
        {
            NotificationService.ShowWarning(Strings.Lock, Strings.LayerIsLocked);
            return;
        }

        s_logger.LogError(result.Failure?.Exception, "Failed to add an element: {FailureId}", result.Failure?.Id);
        NotificationService.ShowError(Strings.AddElement, MessageStrings.UnexpectedError);
    }
}
