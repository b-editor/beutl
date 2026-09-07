using System.ComponentModel;
using System.Globalization;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.Editor;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed record HistoryEntrySummary(string Id, string? Name);

public sealed record HistoryStateResponse(
    IReadOnlyList<HistoryEntrySummary> Applied,
    bool CanUndo,
    bool CanRedo,
    int UndoCount,
    int RedoCount,
    HistoryEntrySummary? NextUndo,
    HistoryEntrySummary? NextRedo,
    string Message);

[McpServerToolType]
public sealed class HistoryTools(AgentSessionManager sessions) : ToolBase
{
    private const int MaxSteps = 50;

    [McpServerTool(Name = "undo")]
    [Description("Reverts the most recent edit transactions on the active session, newest first, and reports what was reverted. Use this to back out an experiment instead of authoring a compensating patch — a compensating patch has to reconstruct prior state by hand, while undo restores it exactly. Every apply_edit, duplicate_object, group_elements, and similar mutation is one transaction. In a LiveEditor session the undo stack is the editor's own, so a step may revert a human's edit rather than yours; call read_history immediately before undo and inspect that response's nextUndo when that matters. LiveEditor undo pauses and drains active preview playback, flushes pending editor work, performs every requested step, and captures the returned snapshot inside one guarded batch. The undo response's applied list reports what was reverted, while its nextUndo is the transaction that remains next. File-backed sessions still need save_project to persist the reverted state.")]
    public ValueTask<ToolResult<HistoryStateResponse>> Undo(
        [Description("How many transactions to revert, newest first. Clamped to 1..50. Stops early when the undo stack empties.")]
        int steps = 1,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => MoveAsync(steps, redo: false, cancellationToken));
    }

    [McpServerTool(Name = "redo")]
    [Description("Re-applies transactions previously reverted by undo, oldest-reverted first, and reports what was re-applied. The redo stack is cleared by any new edit, so redo only works when nothing has been authored since the undo. LiveEditor redo pauses and drains active preview playback, flushes pending editor work, performs every requested step, and captures the returned snapshot inside one guarded batch.")]
    public ValueTask<ToolResult<HistoryStateResponse>> Redo(
        [Description("How many transactions to re-apply. Clamped to 1..50. Stops early when the redo stack empties.")]
        int steps = 1,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => MoveAsync(steps, redo: true, cancellationToken));
    }

    [McpServerTool(Name = "read_history")]
    [Description("Reports the active session's undo/redo depth and the names of the next transaction in each direction without undoing or redoing anything. Pending editor work is flushed first so the reported stack is current. Call this before undo when you need to know what a step would revert. In a LiveEditor session, that flush runs through the shared history guard; preview playback is paused and drained first only when pending work can mutate history.")]
    public ValueTask<ToolResult<HistoryStateResponse>> ReadHistory(
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            IEditingSession session = sessions.RequireSession();
            return await ExecuteHistoryOperationAsync(
                session,
                history => history.HasPendingOperations,
                history => CreateState(history, [], "History state only; nothing was changed."),
                cancellationToken).ConfigureAwait(false);
        });
    }

    private async ValueTask<HistoryStateResponse> MoveAsync(
        int steps,
        bool redo,
        CancellationToken cancellationToken)
    {
        IEditingSession session = sessions.RequireSession();
        int requested = Math.Clamp(steps, 1, MaxSteps);
        HistoryStateResponse state = await ExecuteHistoryOperationAsync(
            session,
            history => (redo ? history.CanRedo : history.CanUndo) || history.HasPendingOperations,
            history =>
            {
                var applied = new List<HistoryEntrySummary>();
                for (int i = 0; i < requested; i++)
                {
                    // Peek first: Undo/Redo pops the transaction, so this is the last point it can be named.
                    HistoryTransaction? next = redo ? history.PeekRedo() : history.PeekUndo();
                    if (next is null || !(redo ? history.Redo() : history.Undo()))
                    {
                        break;
                    }

                    applied.Add(ToSummary(next)!);
                }

                string verb = redo ? "Re-applied" : "Reverted";
                string message = applied.Count == 0
                    ? redo
                        ? "Nothing to redo. The redo stack is cleared by any new edit."
                        : "Nothing to undo. The undo stack is empty for this session."
                    : applied.Count < requested
                        ? $"{verb} {applied.Count} of {requested} requested transactions; the stack emptied first."
                        : $"{verb} {applied.Count} transaction(s).";

                // Capture the point-in-time response before leaving the guarded mutation dispatch.
                return CreateState(history, applied, message);
            },
            cancellationToken).ConfigureAwait(false);

        if (state.Applied.Count > 0 && session is FileEditingSession fileSession)
        {
            fileSession.MarkDirty();
        }

        return state;
    }

    private static HistoryStateResponse CreateState(
        HistoryManager history,
        IReadOnlyList<HistoryEntrySummary> applied,
        string message)
    {
        return new HistoryStateResponse(
            applied,
            history.CanUndo,
            history.CanRedo,
            history.UndoCount,
            history.RedoCount,
            ToSummary(history.PeekUndo()),
            ToSummary(history.PeekRedo()),
            message);
    }

    private static async ValueTask<TResult> ExecuteHistoryOperationAsync<TResult>(
        IEditingSession session,
        Func<HistoryManager, bool> shouldPause,
        Func<HistoryManager, TResult> operation,
        CancellationToken cancellationToken)
    {
        if (session is LiveEditingSession liveSession)
        {
            return await liveSession.ExecuteHistoryMutationAsync(
                shouldPause,
                operation,
                cancellationToken).ConfigureAwait(false);
        }

        TResult result = default!;
        session.InvokeOnSession(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            HistoryManager history = session.History;
            // File sessions have no preview player. They still flush pending work before the
            // operation so the returned stack snapshot has the same semantics as a live session.
            history.FlushPendingMutations();
            result = operation(history);
        });
        return result;
    }

    private static HistoryEntrySummary? ToSummary(HistoryTransaction? transaction)
    {
        return transaction is null
            ? null
            : new HistoryEntrySummary(
                transaction.Id.ToString(CultureInfo.InvariantCulture),
                transaction.DisplayName ?? transaction.Name);
    }
}
