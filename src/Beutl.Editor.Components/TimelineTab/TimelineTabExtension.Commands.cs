using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reactive;
using System.Runtime.InteropServices;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Services.PrimitiveImpls;

// The timeline's commands run on TimelineTabViewModel. Rename, Split and Save as template also run from the
// palette on the editor's selected element, whether or not the timeline is open.
public sealed partial class TimelineTabExtension : IContextCommandEditorStateNotifier
{
    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        Command("timeline.paste", Strings.Paste, Strings.Paste_Description,
            [new("Ctrl+V"), new("Cmd+V", OSPlatform.OSX)], timeline => timeline.Paste.ExecuteAsync()),
        // From their gestures, Rename and Split also run on the focused clip.
        new ContextCommandDefinition<ElementViewModel>("timeline.rename", Strings.Rename, Strings.Rename_Description,
            [new("F2"), new("Enter", OSPlatform.OSX)])
        {
            Scope = ContextCommandScope.Extension,
            CanExecute = execution => TryGetSelectedElement(execution.EditorContext, out Scene? scene, out Element? element)
                && !scene.IsElementLocked(element),
            ExecuteAsync = execution => OnSelectedElement(execution, (editorContext, scene, element) =>
                execution.Interaction is { } interaction
                    ? RenameAsync(editorContext, scene, element, interaction)
                    : Task.CompletedTask),
            ExecuteOnAsync = (element, _) =>
            {
                if (element.IsEditable.Value)
                    element.RenameRequested();
                return Task.CompletedTask;
            }
        },
        // Delete took over Exclude's key. Ctrl+Delete stays first, where keymap.json keeps a changed Delete gesture.
        Command("timeline.delete", Strings.Delete, Strings.Delete_Description,
            [new("Ctrl+Delete"), new("Delete"), new("Cmd+Back", OSPlatform.OSX), new("Back", OSPlatform.OSX)],
            timeline => timeline.SelectedElements.FirstOrDefault()?.Delete.Execute(), HasSelection),
        Command("timeline.copy", Strings.Copy, Strings.Copy_Description,
            [new("Ctrl+C"), new("Cmd+C", OSPlatform.OSX)],
            timeline => timeline.SelectedElements.FirstOrDefault()?.Copy.ExecuteAsync() ?? Task.CompletedTask,
            HasSelection),
        Command("timeline.cut", Strings.Cut, Strings.Cut_Description,
            [new("Ctrl+X"), new("Cmd+X", OSPlatform.OSX)],
            timeline => timeline.SelectedElements.FirstOrDefault()?.Cut.ExecuteAsync() ?? Task.CompletedTask,
            HasSelection),
        new ContextCommandDefinition<ElementViewModel>("timeline.split", Strings.SplitByCurrentFrame, "",
            [new("Ctrl+K"), new("Cmd+K", OSPlatform.OSX)])
        {
            Scope = ContextCommandScope.Extension,
            CanExecute = execution => TryGetSelectedElement(execution.EditorContext, out Scene? scene, out Element? element)
                && CanSplit(execution.EditorContext!, scene, element),
            ExecuteAsync = execution => OnSelectedElement(execution, SplitAsync),
            ExecuteOnAsync = (element, _) =>
            {
                element.SplitByCurrentFrame.Execute();
                return Task.CompletedTask;
            }
        },
        new ContextCommandDefinition("timeline.saveAsTemplate", Strings.SaveAsTemplate, "", [])
        {
            Scope = ContextCommandScope.Extension,
            // Saving a template only reads the element, so a lock does not stop it.
            CanExecute = execution => TryGetSelectedElement(execution.EditorContext, out _, out _),
            ExecuteAsync = execution => OnSelectedElement(execution, (editorContext, scene, element) =>
                execution.Interaction is { } interaction
                    ? SaveAsTemplateAsync(editorContext, scene, element, interaction)
                    : Task.CompletedTask)
        },
        Command("timeline.duplicate", Strings.Duplicate, Strings.Duplicate_Description,
            [new("Ctrl+D"), new("Cmd+D", OSPlatform.OSX)], timeline => timeline.Duplicate.Execute(), HasSelection),
        Shortcut("timeline.setStartTime", Strings.SetStartTime, [new("OemOpenBrackets")],
            timeline => timeline.SetStartTimeToCurrentTime.Execute()),
        Shortcut("timeline.setEndTime", Strings.SetEndTime, [new("OemCloseBrackets")],
            timeline => timeline.SetEndTimeToCurrentTime.Execute()),
        // Nudge uses , and . so that it does not collide with the existing shortcuts on the arrow keys.
        Nudge("timeline.nudgeLeftFrame", Strings.NudgeLeftFrame, [new("OemComma")],
            -1, TimelineTabViewModel.NudgeUnit.Frame),
        Nudge("timeline.nudgeRightFrame", Strings.NudgeRightFrame, [new("OemPeriod")],
            +1, TimelineTabViewModel.NudgeUnit.Frame),
        Nudge("timeline.nudgeLeftLarge", Strings.NudgeLeftLarge, [new("Shift+OemComma")],
            -1, TimelineTabViewModel.NudgeUnit.Large),
        Nudge("timeline.nudgeRightLarge", Strings.NudgeRightLarge, [new("Shift+OemPeriod")],
            +1, TimelineTabViewModel.NudgeUnit.Large),
        Nudge("timeline.nudgeLeftSecond", Strings.NudgeLeftSecond, [new("Alt+OemComma")],
            -1, TimelineTabViewModel.NudgeUnit.Second),
        Nudge("timeline.nudgeRightSecond", Strings.NudgeRightSecond, [new("Alt+OemPeriod")],
            +1, TimelineTabViewModel.NudgeUnit.Second),
        Command("timeline.toggleGroup", Strings.Group, "",
            [new("Ctrl+G"), new("Cmd+G", OSPlatform.OSX)], ToggleGroup,
            timeline => timeline.SelectedElements.FirstOrDefault() is { } first
                && (first.CanUngroupSelectedElements() || first.CanGroupSelectedElements())),
        ToolMode("timeline.toggleRazorMode", Strings.RazorTool, Strings.RazorTool_Description,
            [new("C")], timeline => timeline.IsRazorMode),
        ExitToolMode("timeline.exitRazorMode", Strings.ExitRazorTool, Strings.ExitRazorTool_Description,
            timeline => timeline.IsRazorMode),
        Shortcut("timeline.toggleRippleMode", Strings.RippleEdit, [new("B")],
            timeline => timeline.IsRippleEnabled.Value = !timeline.IsRippleEnabled.Value),
        ToolMode("timeline.toggleSlipMode", Strings.SlipTool, Strings.SlipTool_Description,
            [new("S")], timeline => timeline.IsSlipMode),
        ExitToolMode("timeline.exitSlipMode", Strings.ExitSlipTool, Strings.ExitSlipTool_Description,
            timeline => timeline.IsSlipMode),
        ToolMode("timeline.toggleRollMode", Strings.RollTool, Strings.RollTool_Description,
            [new("R")], timeline => timeline.IsRollMode),
        ExitToolMode("timeline.exitRollMode", Strings.ExitRollTool, Strings.ExitRollTool_Description,
            timeline => timeline.IsRollMode),
        ToolMode("timeline.toggleSlideMode", Strings.SlideTool, Strings.SlideTool_Description,
            [new("D")], timeline => timeline.IsSlideMode),
        ExitToolMode("timeline.exitSlideMode", Strings.ExitSlideTool, Strings.ExitSlideTool_Description,
            timeline => timeline.IsSlideMode),
        Shortcut("timeline.closeGap", Strings.CloseGap, [new("OemSemicolon")],
            timeline => timeline.CloseGap.Execute(), HasSelection),
        Shortcut("timeline.closeAllGaps", Strings.CloseAllGaps, [new("Shift+Alt+OemSemicolon")],
            timeline => timeline.CloseAllGaps.Execute()),
        Shortcut("timeline.goToNextGap", Strings.GoToNextGap, [new("Shift+OemSemicolon")],
            timeline => timeline.GoToNextGap.Execute()),
        Shortcut("timeline.goToPreviousGap", Strings.GoToPreviousGap, [new("Alt+OemSemicolon")],
            timeline => timeline.GoToPreviousGap.Execute()),
    ];

    private static bool HasSelection(TimelineTabViewModel timeline) => timeline.SelectedElements.Count > 0;

    // A timeline command first commits a pending nudge, so the two do not end up in one undo step.
    private static ContextCommandDefinition<TimelineTabViewModel> Command(
        string id, string displayName, string description, ContextCommandKeyGesture[] keyGestures,
        Func<TimelineTabViewModel, Task> execute, Func<TimelineTabViewModel, bool>? canExecute = null)
        => new(id, displayName, description, keyGestures)
        {
            CanExecuteOn = canExecute is null ? null : (timeline, _) => canExecute(timeline),
            ExecuteOnAsync = (timeline, _) =>
            {
                timeline.FlushPendingNudgeCommit();
                return execute(timeline);
            }
        };

    private static ContextCommandDefinition<TimelineTabViewModel> Command(
        string id, string displayName, string description, ContextCommandKeyGesture[] keyGestures,
        Action<TimelineTabViewModel> execute, Func<TimelineTabViewModel, bool>? canExecute = null)
        => Command(id, displayName, description, keyGestures, timeline => { execute(timeline); return Task.CompletedTask; }, canExecute);

    // A shortcut on a printable key, which does not fire while a text box has the key.
    private static ContextCommandDefinition<TimelineTabViewModel> Shortcut(
        string id, string displayName, ContextCommandKeyGesture[] keyGestures,
        Action<TimelineTabViewModel> execute, Func<TimelineTabViewModel, bool>? canExecute = null)
        => Shortcut(id, displayName, "", keyGestures, execute, canExecute);

    private static ContextCommandDefinition<TimelineTabViewModel> Shortcut(
        string id, string displayName, string description, ContextCommandKeyGesture[] keyGestures,
        Action<TimelineTabViewModel> execute, Func<TimelineTabViewModel, bool>? canExecute, bool flushNudge = true)
        => new(id, displayName, description, keyGestures)
        {
            CanExecuteOn = (timeline, execution) =>
                !TimelineTabViewModel.IsTextInputSource(execution.KeyEventArgs?.Source)
                && (canExecute?.Invoke(timeline) ?? true),
            ExecuteOnAsync = (timeline, _) =>
            {
                if (flushNudge)
                    timeline.FlushPendingNudgeCommit();
                execute(timeline);
                return Task.CompletedTask;
            }
        };

    // Repeated nudges collect into one undo step, so a nudge does not commit the pending one.
    private static ContextCommandDefinition<TimelineTabViewModel> Nudge(
        string id, string displayName, ContextCommandKeyGesture[] keyGestures,
        int direction, TimelineTabViewModel.NudgeUnit unit)
        => Shortcut(id, displayName, "", keyGestures,
            timeline => timeline.NudgeSelectedElements(direction, unit), HasSelection, flushNudge: false);

    private static ContextCommandDefinition<TimelineTabViewModel> ToolMode(
        string id, string displayName, string description, ContextCommandKeyGesture[] keyGestures,
        Func<TimelineTabViewModel, ReactivePropertySlim<bool>> mode)
        => Shortcut(id, displayName, description, keyGestures,
            timeline => mode(timeline).Value = !mode(timeline).Value, null);

    // An exit key is taken only while its mode is on.
    private static ContextCommandDefinition<TimelineTabViewModel> ExitToolMode(
        string id, string displayName, string description,
        Func<TimelineTabViewModel, ReactivePropertySlim<bool>> mode)
        => Shortcut(id, displayName, description, [new("V"), new("Escape")],
            timeline => mode(timeline).Value = false, timeline => mode(timeline).Value);

    private static void ToggleGroup(TimelineTabViewModel timeline)
    {
        ElementViewModel? first = timeline.SelectedElements.FirstOrDefault();
        if (first?.CanUngroupSelectedElements() == true)
        {
            first.UngroupSelectedElements.Execute();
        }
        else if (first?.CanGroupSelectedElements() == true)
        {
            first.GroupSelectedElements.Execute();
        }
    }

    private static Task OnSelectedElement(
        ContextCommandExecution execution, Func<IEditorContext, Scene, Element, Task> execute)
        => TryGetSelectedElement(execution.EditorContext, out Scene? scene, out Element? element)
            ? execute(execution.EditorContext!, scene, element)
            : Task.CompletedTask;

    // The commands act on the selected element, and Split also on the playhead, which moves every frame
    // during playback.
    public IObservable<Unit> GetCanExecuteChanged(IEditorContext editorContext)
    {
        IObservable<Unit> selection = editorContext.GetService<IEditorSelection>()?.SelectedObject
            .Select(_ => Unit.Default) ?? Observable.Empty<Unit>();
        IObservable<Unit> time = editorContext.GetService<IEditorClock>()?.CurrentTime
            .Select(_ => Unit.Default) ?? Observable.Empty<Unit>();
        return selection.Merge(time);
    }

    private static async Task RenameAsync(
        IEditorContext editorContext, Scene scene, Element element, IContextCommandInteraction interaction)
    {
        string? name = await interaction.ShowInputAsync(new ContextCommandInputOptions { Value = element.Name.Value });
        if (name is null || !scene.Children.Contains(element) || scene.IsElementLocked(element)) return;

        // An empty name is kept: it leaves the element unnamed.
        editorContext.GetRequiredService<IElementAttributeService>().SetName(element, name.Trim());
    }

    private static Task SplitAsync(IEditorContext editorContext, Scene scene, Element element)
    {
        // An open timeline splits the way Ctrl+K on the clip does: every selected clip and its group.
        if (editorContext.FindToolTab<TimelineTabViewModel>()?.GetViewModelFor(element) is { } viewModel)
        {
            viewModel.SplitByCurrentFrame.Execute();
        }
        else if (GetSplitTargets(editorContext, scene, element) is { Length: > 0 } targets)
        {
            editorContext.GetRequiredService<IElementStructureService>()
                .Split(scene, targets, GetSplitTime(editorContext, scene));
        }

        return Task.CompletedTask;
    }

    // Split is available when any clip it would act on can be split at the playhead, not only the selected
    // one. Like IElementStructureService.Split, both halves have to be at least one frame long.
    private static bool CanSplit(IEditorContext editorContext, Scene scene, Element element)
    {
        MediaTime time = GetSplitTime(editorContext, scene);
        return GetSplitTargets(editorContext, scene, element)
            .Any(e => ElementStructureService.LeavesAFrameOnEachSide(scene, e, time));
    }

    // The editable clips a split acts on. An open timeline uses the clip's own rule (its selection and
    // group, the way Ctrl+K does); otherwise the selected element and its group.
    private static Element[] GetSplitTargets(IEditorContext editorContext, Scene scene, Element element)
    {
        if (editorContext.FindToolTab<TimelineTabViewModel>()?.GetViewModelFor(element) is { } viewModel)
        {
            IReadOnlyList<ElementViewModel> selected = viewModel.GetGroupOrSelectedElements();
            return (selected.Count == 0 ? [viewModel] : selected)
                .Where(e => e.IsEditable.Value)
                .Select(e => e.Model)
                .ToArray();
        }

        ImmutableHashSet<Guid>? group = scene.Groups.FirstOrDefault(g => g.Contains(element.Id.Value));
        return (group is null ? [element] : scene.Children.Where(e => group.Contains(e.Id.Value)))
            .Where(e => !scene.IsElementLocked(e))
            .ToArray();
    }

    // The clips a template saves, locked ones too, since saving only reads them. An open timeline uses the clip's
    // own rule (its selection and group, the clips Ctrl+C copies); otherwise the editor's selection, which outlives
    // the timeline, with the selected element and each clip's group.
    private static Element[] GetTemplateTargets(IEditorContext editorContext, Scene scene, Element element)
    {
        if (editorContext.FindToolTab<TimelineTabViewModel>()?.GetViewModelFor(element) is { } viewModel)
        {
            IReadOnlyList<ElementViewModel> selected = viewModel.GetGroupOrSelectedElements();
            return (selected.Count == 0 ? [viewModel] : selected)
                .Select(e => e.Model)
                .ToArray();
        }

        IEnumerable<Guid> selection = editorContext.GetService<IEditorSelection>()?.SelectedElements
            .Select(e => e.Id.Value) ?? [];
        HashSet<Guid> ids = DuplicateHelper.ExpandWithGroupSiblings(selection.Append(element.Id.Value), scene.Groups);
        return scene.Children.Where(e => ids.Contains(e.Id.Value)).ToArray();
    }

    private static async Task SaveAsTemplateAsync(
        IEditorContext editorContext, Scene scene, Element element, IContextCommandInteraction interaction)
    {
        // Taken before the name is asked for, so the template holds the clips the command was run on.
        Element[] targets = GetTemplateTargets(editorContext, scene, element);
        string? name = await interaction.ShowInputAsync(new ContextCommandInputOptions
        {
            Value = GetDefaultTemplateName(element),
            Validate = ValidateTemplateName
        });
        if (name is null) return;

        // Switching editors cancels the command, which stops rendering the preview and saving.
        await TemplateSaving.SaveAsync(
            TemplateSaving.CreateElementTemplate(scene, targets),
            name.Trim(),
            cancellationToken: interaction.CancellationToken);
    }

    // The element's name, or its type's when it has none, made unique among the templates.
    internal static string GetDefaultTemplateName(Element element)
    {
        string defaultName = !string.IsNullOrWhiteSpace(element.Name.Value)
            ? element.Name.Value
            : TypeDisplayHelpers.GetLocalizedName(element.GetType());
        return ObjectTemplateService.Instance.GetUniqueName(defaultName);
    }

    // The template is stored under its name, so the name has to be a valid file name.
    private static string? ValidateTemplateName(string value)
    {
        if (CommandPaletteInput.Required(value) is { } error) return error;

        return ObjectTemplateService.IsValidTemplateName(value.Trim()) ? null : MessageStrings.InvalidString;
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

    private static MediaTime GetSplitTime(IEditorContext editorContext, Scene scene)
    {
        MediaTime time = editorContext.GetService<IEditorClock>()?.CurrentTime.Value ?? MediaTime.Zero;
        return time.RoundToFrame(SceneTimeRangeService.GetFrameRate(scene), MidpointRounding.AwayFromZero);
    }
}
