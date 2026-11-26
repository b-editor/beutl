using Avalonia.Controls;
using Avalonia.Input;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using static Avalonia.Input.KeyModifiers;
using static Beutl.Editor.Components.Helpers.EditorShortcutGestures;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public partial class GraphEditorView
{
    private const KeyModifiers WithoutShift = AllModifiers & ~Shift;

    // The previous key handler accepted extra modifiers on most chords. Keep each chord in
    // its original action, including Shift+J, which would otherwise reach the editor's shuttle.
    internal static IReadOnlyList<ContextCommandDefinition> Commands =>
    [
        new ContextCommandDefinition<GraphEditorView>("graphEditor.pan", Strings.PanView, "", Gestures(Key.Space, optional: AllModifiers))
        {
            CanExecuteOn = (_, execution) => execution.KeyEventArgs is not null,
            ExecuteOnAsync = (view, execution) =>
            {
                view._space.Press(execution.KeyEventArgs!, !view.IsGraphGestureActive && view._mouseFlag == TimelineHelper.MouseFlags.Free);
                return Task.CompletedTask;
            }
        },
        Command("cancel", Strings.Cancel, Gestures(Key.Escape, optional: AllModifiers),
            (view, model, channel) => view.CancelKeyGesture(model, channel), duringGesture: true),
        Command("selectAll", Strings.SelectAll, WithCommand(Key.A, optional: WithoutShift),
            (_, _, channel) => channel.SetSelection(channel.KeyFrames.Select(x => x.Model))),
        Command("deselectAll", Strings.DeselectAll, [.. WithCommand(Key.A, Shift, AllModifiers), .. Gestures(Key.F2, Shift, AllModifiers)],
            (_, _, channel) => channel.SetSelection([])),
        AsyncCommand("copy", Strings.Copy, WithCommand(Key.C, optional: AllModifiers),
            (view, model, _) => model.CopySelectionAsync(TopLevel.GetTopLevel(view)?.Clipboard)),
        AsyncCommand("cut", Strings.Cut, WithCommand(Key.X, optional: AllModifiers),
            (view, _, _) => view.CutSelectionAsync(TopLevel.GetTopLevel(view)?.Clipboard)),
        AsyncCommand("paste", Strings.Paste, WithCommand(Key.V, optional: AllModifiers),
            (view, model, _) => model.PasteSelectionAsync(TopLevel.GetTopLevel(view)?.Clipboard)),
        Command("delete", Strings.Delete,
            [.. Gestures(Key.Delete, optional: AllModifiers), .. Gestures(Key.Back, optional: AllModifiers)],
            (view, _, _) => view.DeleteSelection()),
        Command("velocity", Strings.GraphVelocity, WithCommand(Key.K, Shift, AllModifiers),
            (view, _, _) => view.ShowVelocityFlyout()),
        Command("ease", Strings.GraphEasyEase, WithoutCommand(Key.F9, None, WithoutShift),
            (view, _, _) => view.ApplyEasing("Ease")),
        Command("easeIn", Strings.GraphEasyEaseIn, Gestures(Key.F9, Shift, AllModifiers),
            (view, _, _) => view.ApplyEasing("EaseIn")),
        Command("easeOut", Strings.GraphEasyEaseOut, WithCommand(Key.F9, optional: WithoutShift),
            (view, _, _) => view.ApplyEasing("EaseOut")),
        Command("distributeEvenly", Strings.GraphDistributeEvenly, Gestures(Key.D, Alt),
            (view, _, _) => view.DistributeSelectionEvenly()),
        Command("reverse", Strings.GraphReverse, Gestures(Key.R, Alt), (view, _, _) => view.ReverseSelection()),
        Nudge("nudgeLeftFrame", Strings.NudgeLeftFrame, Key.Left, -1, Alt, WithoutShift),
        Nudge("nudgeRightFrame", Strings.NudgeRightFrame, Key.Right, 1, Alt, WithoutShift),
        Nudge("nudgeLeftLarge", Strings.NudgeLeftLarge, Key.Left, -10, Alt | Shift, AllModifiers),
        Nudge("nudgeRightLarge", Strings.NudgeRightLarge, Key.Right, 10, Alt | Shift, AllModifiers),
        Command("previousKeyFrame", Strings.PreviousKeyFrame, Gestures(Key.J, optional: AllModifiers),
            (_, model, channel) => JumpToAdjacentKeyFrame(model, channel, forward: false)),
        Command("nextKeyFrame", Strings.NextKeyFrame,
            [.. Gestures(Key.K, optional: WithoutShift), .. WithoutCommand(Key.K, Shift, AllModifiers)],
            (_, model, channel) => JumpToAdjacentKeyFrame(model, channel, forward: true)),
        Command("fitAll", Strings.GraphFitAll, Gestures(Key.F, optional: WithoutShift), (view, _, _) => view.FitGraph(false)),
        Command("fitSelection", Strings.GraphFitSelection, Gestures(Key.F, Shift, AllModifiers), (view, _, _) => view.FitGraph(true)),
        // Use the editor to pause playback before replaying history and to report any failure.
        AsyncCommand("undo", Strings.Undo, WithCommand(Key.Z, optional: WithoutShift),
            (_, model, _) => ((IUndoRedoEditorContext)model.EditorContext).UndoAsync().AsTask(),
            canExecute: model => model.EditorContext is IUndoRedoEditorContext),
        AsyncCommand("redo", Strings.Redo, WithCommand(Key.Z, Shift, AllModifiers),
            (_, model, _) => ((IUndoRedoEditorContext)model.EditorContext).RedoAsync().AsTask(),
            canExecute: model => model.EditorContext is IUndoRedoEditorContext)
    ];

    private bool IsGraphGestureActive => _interactionPointer != null || ControlPointMoveState != null;

    private static ContextCommandDefinition Command(string id, string name, ContextCommandKeyGesture[] gestures,
        Action<GraphEditorView, GraphEditorViewModel, GraphEditorViewViewModel> run, bool duringGesture = false)
        => AsyncCommand(id, name, gestures, (view, model, channel) =>
        {
            run(view, model, channel);
            return Task.CompletedTask;
        }, duringGesture);

    private static ContextCommandDefinition AsyncCommand(string id, string name, ContextCommandKeyGesture[] gestures,
        Func<GraphEditorView, GraphEditorViewModel, GraphEditorViewViewModel, Task> run,
        bool duringGesture = false, Func<GraphEditorViewModel, bool>? canExecute = null)
        => new ContextCommandDefinition<GraphEditorView>($"graphEditor.{id}", name, "", gestures)
        {
            CanExecuteOn = (view, _) => (duringGesture || !view.IsGraphGestureActive)
                && view.DataContext is GraphEditorViewModel { SelectedView.Value: not null } model
                && (canExecute?.Invoke(model) ?? true),
            ExecuteOnAsync = async (view, _) =>
            {
                if (view.DataContext is not GraphEditorViewModel { SelectedView.Value: { } channel } model) return;
                await run(view, model, channel);
                view.UpdateSelectionAdorner();
            }
        };

    private static ContextCommandDefinition Nudge(
        string id, string name, Key key, int frames, KeyModifiers required, KeyModifiers optional)
        => Command(id, name, Gestures(key, required, optional), (_, model, channel) => NudgeSelection(model, channel, frames));
}
