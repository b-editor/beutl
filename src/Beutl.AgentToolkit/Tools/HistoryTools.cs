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
    [Description("Reverts the most recent edit transactions on the active session, newest first, and reports what was reverted. Use this to back out an experiment instead of authoring a compensating patch — a compensating patch has to reconstruct prior state by hand, while undo restores it exactly. Every apply_edit, duplicate_object, group_elements, and similar mutation is one transaction. In a LiveEditor session the undo stack is the editor's own, so a step may revert a human's edit rather than yours; read the returned nextUndo first when that matters. File-backed sessions still need save_project to persist the reverted state.")]
    public ToolResult<HistoryStateResponse> Undo(
        [Description("How many transactions to revert, newest first. Clamped to 1..50. Stops early when the undo stack empties.")]
        int steps = 1)
    {
        return Execute(() => Move(steps, redo: false));
    }

    [McpServerTool(Name = "redo")]
    [Description("Re-applies transactions previously reverted by undo, oldest-reverted first, and reports what was re-applied. The redo stack is cleared by any new edit, so redo only works when nothing has been authored since the undo.")]
    public ToolResult<HistoryStateResponse> Redo(
        [Description("How many transactions to re-apply. Clamped to 1..50. Stops early when the redo stack empties.")]
        int steps = 1)
    {
        return Execute(() => Move(steps, redo: true));
    }

    [McpServerTool(Name = "read_history")]
    [Description("Reports the active session's undo/redo depth and the names of the next transaction in each direction, without changing anything. Call this before undo when you need to know what a step would revert.")]
    public ToolResult<HistoryStateResponse> ReadHistory()
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            return session.ReadOnSession(() => CreateState(session, [], "History state only; nothing was changed."));
        });
    }

    private HistoryStateResponse Move(int steps, bool redo)
    {
        IEditingSession session = sessions.RequireSession();
        int requested = Math.Clamp(steps, 1, MaxSteps);
        List<HistoryEntrySummary> applied = [];

        session.InvokeOnSession(() =>
        {
            HistoryManager history = session.History;
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
        });

        if (applied.Count > 0 && session is FileEditingSession fileSession)
        {
            fileSession.MarkDirty();
        }

        string verb = redo ? "Re-applied" : "Reverted";
        string message = applied.Count == 0
            ? redo
                ? "Nothing to redo. The redo stack is cleared by any new edit."
                : "Nothing to undo. The undo stack is empty for this session."
            : applied.Count < requested
                ? $"{verb} {applied.Count} of {requested} requested transactions; the stack emptied first."
                : $"{verb} {applied.Count} transaction(s).";

        return session.ReadOnSession(() => CreateState(session, applied, message));
    }

    private static HistoryStateResponse CreateState(
        IEditingSession session,
        IReadOnlyList<HistoryEntrySummary> applied,
        string message)
    {
        HistoryManager history = session.History;
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

    private static HistoryEntrySummary? ToSummary(HistoryTransaction? transaction)
    {
        return transaction is null
            ? null
            : new HistoryEntrySummary(
                transaction.Id.ToString(CultureInfo.InvariantCulture),
                transaction.DisplayName ?? transaction.Name);
    }
}
