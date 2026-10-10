using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Operations;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Reactive.Bindings;

namespace Beutl.AgentHost;

// Experimental "follow" mode: while AiAgentConfig.FollowLiveMcpEdits is on, the editor jumps to
// whatever a live MCP client just edited or rendered (scene tab, selection, playhead, timeline
// scroll, property editor), so the user can watch the agent work.
public sealed class AgentEditFollower(EditorService editorService, AiAgentConfig config)
{
    public bool IsEnabled => config.FollowLiveMcpEdits;

    // The undo stack top and redo depth, taken on the UI thread before an agent call runs.
    internal readonly record struct HistoryMark(HistoryTransaction? UndoTop, int RedoCount);

    private sealed record EditTarget(
        Element? Element,
        CoreObject? Object,
        string? PropertyName,
        TimeSpan? Time,
        TimeRange? RemovedRange = null,
        int RemovedZIndex = 0);

    internal HistoryMark? Capture(EditViewModel editor)
    {
        if (!IsEnabled || editor.IsDisposingOrDisposed)
            return null;

        try
        {
            return new HistoryMark(editor.HistoryManager.PeekUndo(), editor.HistoryManager.RedoCount);
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    // Finds the transaction the agent call committed, undid or redid, and navigates to it once the
    // call has returned.
    internal void FollowHistoryChange(EditViewModel editor, HistoryMark mark)
    {
        if (editor.IsDisposingOrDisposed)
            return;

        HistoryTransaction? changed;
        try
        {
            HistoryManager history = editor.HistoryManager;
            HistoryTransaction? undoTop = history.PeekUndo();
            // Undo, by any number of steps, deepens the redo stack and leaves the last reverted
            // transaction on top of it; a commit or a redo puts another transaction on top of the
            // undo stack.
            if (history.RedoCount > mark.RedoCount)
                changed = history.PeekRedo();
            else if (!ReferenceEquals(undoTop, mark.UndoTop))
                changed = undoTop;
            else
                return;

            if (changed is null)
                return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (ResolveTarget(editor.Scene, changed.Operations) is { } target)
            Dispatcher.UIThread.Post(() => NavigateToEdit(editor, target), DispatcherPriority.Normal);
    }

    // render_still is the agent's single-frame check, so show the user the same frame. Register
    // ahead of AgentHostSceneRouter, which strips sceneId from the arguments.
    internal static void AddFilters(IMcpRequestFilterBuilder filters)
    {
        filters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            if (context.Params is not { Name: "render_still", Arguments: { } arguments }
                || !arguments.TryGetValue("sceneId", out JsonElement id)
                || id.ValueKind != JsonValueKind.String
                || !Guid.TryParse(id.GetString(), out Guid sceneId)
                || context.Services?.GetService<AgentEditFollower>() is not { IsEnabled: true } follower)
            {
                return await next(context, cancellationToken);
            }

            double seconds = 0;
            if (arguments.TryGetValue("timeSeconds", out JsonElement time)
                && !(time.TryGetDouble(out seconds) && seconds is >= 0 and < 1e9))
            {
                return await next(context, cancellationToken);
            }

            // Seek before rendering when the scene already has a tab. A closed scene only gets
            // its background tab while the call resolves its target, so seek after it instead.
            if (await follower.ShowFrameAsync(sceneId, TimeSpan.FromSeconds(seconds)))
                return await next(context, cancellationToken);

            CallToolResult result = await next(context, cancellationToken);
            await follower.ShowFrameAsync(sceneId, TimeSpan.FromSeconds(seconds));
            return result;
        });
    }

    // `sceneTime` is relative to the scene start, as in render_still. Returns false when the scene
    // has no editor tab yet.
    internal async Task<bool> ShowFrameAsync(Guid sceneId, TimeSpan sceneTime)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (FindEditor(sceneId) is not { } editor)
                return false;
            if (!CanNavigate(editor))
                return true;

            Activate(editor);
            if (editor.Player.IsPlaying.Value)
                return true;

            Scene scene = editor.Scene;
            int rate = editor.Player.GetFrameRate();
            TimeSpan last = (scene.Start + scene.Duration - TimeSpan.FromTicks(1)).FloorToRate(rate);
            TimeSpan time = (scene.Start + sceneTime).RoundToRate(rate);
            editor.SeekAndScroll(Clamp(time, scene.Start, last));
            return true;
        }, DispatcherPriority.Normal);
    }

    private bool CanNavigate(EditViewModel editor)
        => IsEnabled && !editor.IsDisposingOrDisposed && editor.IsEnabled.Value;

    private EditViewModel? FindEditor(Guid sceneId)
        => editorService.TabItems
            .Select(item => item.Context.Value)
            .OfType<EditViewModel>()
            .FirstOrDefault(editor => !editor.IsDisposingOrDisposed && editor.Scene?.Id == sceneId);

    // Live MCP binds closed scenes to unselected background tabs; bring the edited one forward.
    private void Activate(EditViewModel editor)
    {
        EditorTabItem? tab = editorService.TabItems.FirstOrDefault(item => ReferenceEquals(item.Context.Value, editor));
        if (tab is null || ReferenceEquals(editorService.SelectedTabItem.Value, tab))
            return;

        tab.IsSelected.Value = true;
        editorService.SelectedTabItem.Value = tab;
    }

    private void NavigateToEdit(EditViewModel editor, EditTarget target)
    {
        if (!CanNavigate(editor))
            return;

        Activate(editor);
        TimelineTabViewModel? timeline = editor.FindToolTab<TimelineTabViewModel>();
        if (target.Element is not { } element)
        {
            // Nothing left to select; show where the removed element was.
            if (target.RemovedRange is { } removed)
                timeline?.ScrollTo.Execute((removed, target.RemovedZIndex));
            return;
        }

        // Following an edit to the element already selected still tells the editor, which reopens a closed
        // element property tab on it.
        IReactiveProperty<CoreObject?> selection = editor.GetRequiredService<IEditorSelection>().SelectedObject;
        if (ReferenceEquals(selection.Value, element))
            selection.ForceNotify();
        else
            selection.Value = element;
        if (!editor.Player.IsPlaying.Value)
        {
            IEditorClock clock = editor.GetRequiredService<IEditorClock>();
            int rate = editor.Player.GetFrameRate();
            TimeSpan last = (element.Range.End - TimeSpan.FromTicks(1)).FloorToRate(rate);
            // Keep the playhead when it already shows the element; otherwise move it the least.
            TimeSpan time = Clamp(target.Time ?? clock.CurrentTime.Value, element.Start, last);
            if (clock.CurrentTime.Value != time)
                clock.CurrentTime.Value = time;
        }

        if (timeline is not null)
        {
            timeline.ScrollTo.Execute((element.Range, element.ZIndex));
            timeline.HighlightElement.Execute(element);
        }

        // Selecting the element above opened it in an element property tab, possibly next to tabs pinned to
        // other elements, so reveal the edit in the tab showing this element.
        if (target.Object is { } edited && !ReferenceEquals(edited, element)
            && editor.FindToolTab<ElementPropertyTabViewModel>(t => t.Element.Value == element) is { } properties)
        {
            properties.IsSelected.Value = true;
            properties.Reveal(edited, target.PropertyName);
        }

        // Selection alone does not redraw the preview, and its bounds outline the selected layer.
        editor.Player.QueuePreviewRender();
    }

    private static EditTarget? ResolveTarget(Scene scene, IReadOnlyList<ChangeOperation> operations)
    {
        // apply_edit deserializes the nested objects (brushes, transforms, effects) of every element
        // it writes, so its transaction also swaps untouched values for equal copies, and the copies
        // then take on their element's time range and layer. Skip both to find the real change.
        var swappedIn = new HashSet<CoreObject>(ReferenceEqualityComparer.Instance);
        EditTarget? removed = null;
        foreach (ChangeOperation operation in operations)
        {
            switch (operation)
            {
                case IUpdatePropertyValueOperation update:
                    if (IsWithin(update.Object, swappedIn))
                        break;
                    if (update.NewValue is CoreObject replacement)
                    {
                        swappedIn.Add(replacement);
                        if (IsEqualCopy(update.OldValue, replacement))
                            break;
                    }

                    if (CreateTarget(scene, update.Object, PropertyNameOf(update.PropertyPath)) is { } updated)
                        return updated;
                    break;

                case ICollectionChangeOperation collection:
                    foreach (object? item in collection.Items)
                    {
                        // Inserted items are attached; removed ones are not.
                        if (item is CoreObject child && CreateTarget(scene, child, null) is { } inserted)
                            return inserted;
                        if (item is Element gone)
                            removed ??= new EditTarget(null, null, null, null, gone.Range, gone.ZIndex);
                    }

                    if (CreateTarget(scene, collection.Object, PropertyNameOf(collection.PropertyPath)) is { } owner)
                        return owner;
                    break;
            }
        }

        return removed;
    }

    private static bool IsWithin(CoreObject obj, HashSet<CoreObject> roots)
    {
        if (roots.Contains(obj))
            return true;

        for (IHierarchical? current = (obj as IHierarchical)?.HierarchicalParent; current is not null; current = current.HierarchicalParent)
        {
            if (current is CoreObject core && roots.Contains(core))
                return true;
        }

        return false;
    }

    private static bool IsEqualCopy(object? original, CoreObject copy)
        => original is CoreObject source
           && source.GetType() == copy.GetType()
           && JsonNode.DeepEquals(CoreSerializer.SerializeToJsonObject(source), CoreSerializer.SerializeToJsonObject(copy));

    private static EditTarget? CreateTarget(Scene scene, CoreObject obj, string? propertyName)
    {
        Element? element = obj as Element
                           ?? (obj as IHierarchical)?.EnumerateAncestors<Element>().FirstOrDefault();
        if (element is null || !ReferenceEquals(element.HierarchicalParent, scene))
            return null;

        return new EditTarget(element, obj, propertyName, KeyFrameTime(obj, element));
    }

    private static TimeSpan? KeyFrameTime(CoreObject obj, Element element)
    {
        if (obj is not IKeyFrame keyFrame
            || keyFrame.EnumerateAncestors<IAnimation>().FirstOrDefault() is not { } animation)
        {
            return null;
        }

        return animation.UseGlobalClock ? keyFrame.KeyTime : element.Start + keyFrame.KeyTime;
    }

    // Matches UpdatePropertyValueOperation: "Opacity", "Transform.X", "Opacity.Animation".
    private static string PropertyNameOf(string propertyPath)
    {
        string[] parts = propertyPath.Split('.');
        return parts.Length >= 2 && parts[^1] is "Animation" or "Expression" ? parts[^2] : parts[^1];
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
        => max < min ? min : value < min ? min : value > max ? max : value;
}
