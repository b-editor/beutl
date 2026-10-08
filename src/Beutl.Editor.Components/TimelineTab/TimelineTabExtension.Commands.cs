using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Services.PrimitiveImpls;

// Handles the extension-scoped commands, which act on the editor's selected element whether or not the
// timeline is open. The timeline's own commands are handled by TimelineTabViewModel.
public sealed partial class TimelineTabExtension : IContextCommandHandler
{
    public bool CanExecute(ContextCommandExecution execution)
    {
        if (!TryGetSelectedElement(execution.EditorContext, out Scene? scene, out Element? element))
            return false;

        return execution.CommandName switch
        {
            "Rename" => !scene.IsElementLocked(element),
            "Split" => !scene.IsElementLocked(element) && IsInside(element, GetSplitTime(execution.EditorContext!, scene)),
            // Saving a template only reads the element, so a lock does not stop it.
            "SaveAsTemplate" => true,
            _ => false,
        };
    }

    public Task ExecuteAsync(ContextCommandExecution execution)
    {
        if (!CanExecute(execution)
            || !TryGetSelectedElement(execution.EditorContext, out Scene? scene, out Element? element))
        {
            return Task.CompletedTask;
        }

        IEditorContext editorContext = execution.EditorContext!;
        return execution.CommandName switch
        {
            "Rename" when execution.Interaction is { } interaction =>
                RenameAsync(editorContext, scene, element, interaction),
            "Split" => SplitAsync(editorContext, scene, element),
            "SaveAsTemplate" when execution.Interaction is { } interaction =>
                SaveAsTemplateAsync(element, interaction),
            _ => Task.CompletedTask,
        };
    }

    private static async Task RenameAsync(
        IEditorContext editorContext, Scene scene, Element element, IContextCommandInteraction interaction)
    {
        string? name = await interaction.ShowInputAsync(new ContextCommandInputOptions { Value = element.Name });
        if (name is null || !scene.Children.Contains(element) || scene.IsElementLocked(element)) return;

        editorContext.GetRequiredService<IElementAttributeService>().SetName(element, name);
    }

    private static async Task SplitAsync(IEditorContext editorContext, Scene scene, Element element)
    {
        // An open timeline splits the way Ctrl+K on the clip does: every selected clip and its group.
        if (editorContext.FindToolTab<TimelineTabViewModel>()?.GetViewModelFor(element) is { } viewModel)
        {
            await viewModel.ExecuteAsync(new ContextCommandExecution("Split"));
            return;
        }

        ImmutableHashSet<Guid>? group = scene.Groups.FirstOrDefault(g => g.Contains(element.Id));
        Element[] targets = (group is null ? [element] : scene.Children.Where(e => group.Contains(e.Id)))
            .Where(e => !scene.IsElementLocked(e))
            .ToArray();
        if (targets.Length == 0) return;

        editorContext.GetRequiredService<IElementStructureService>()
            .Split(scene, targets, GetSplitTime(editorContext, scene));
    }

    private static async Task SaveAsTemplateAsync(Element element, IContextCommandInteraction interaction)
    {
        string defaultName = !string.IsNullOrWhiteSpace(element.Name)
            ? element.Name
            : TypeDisplayHelpers.GetLocalizedName(element.GetType());
        string? name = await interaction.ShowInputAsync(new ContextCommandInputOptions
        {
            Value = ObjectTemplateService.Instance.GetUniqueName(defaultName),
            Validate = ValidateTemplateName
        });
        if (name is null) return;

        try
        {
            if (await ObjectTemplateService.Instance.AddFromInstanceAsync(element, name.Trim()) is not null)
                return;
        }
        catch (Exception)
        {
        }

        NotificationService.ShowError(Strings.SaveAsTemplate, MessageStrings.OperationFailed);
    }

    // The template is stored under its name, so the name has to be a valid file name.
    private static string? ValidateTemplateName(string value)
    {
        if (CommandPaletteInput.Required(value) is { } error) return error;

        string name = value.Trim();
        return name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains('/') || name.Contains('\\')
            ? MessageStrings.InvalidString
            : null;
    }

    private static bool TryGetSelectedElement(
        IEditorContext? editorContext,
        [NotNullWhen(true)] out Scene? scene,
        [NotNullWhen(true)] out Element? element)
    {
        scene = editorContext?.GetService<Scene>();
        element = editorContext?.GetService<IEditorSelection>()?.SelectedObject.Value as Element;
        return scene is not null && element is not null && scene.Children.Contains(element);
    }

    private static TimeSpan GetSplitTime(IEditorContext editorContext, Scene scene)
    {
        TimeSpan time = editorContext.GetService<IEditorClock>()?.CurrentTime.Value ?? TimeSpan.Zero;
        return time.RoundToRate(scene.FindHierarchicalParent<Project>().GetFrameRate());
    }

    private static bool IsInside(Element element, TimeSpan time)
    {
        return element.Start < time && time < element.Range.End;
    }
}
