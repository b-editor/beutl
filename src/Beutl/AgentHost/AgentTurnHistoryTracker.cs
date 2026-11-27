using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Beutl.Editor;
using Beutl.Editor.Components.AgentTab;

namespace Beutl.AgentHost;

internal sealed class AgentTurnHistoryTracker
{
    private static readonly AsyncLocal<Turn?> s_requestTurn = new();
    private readonly ConcurrentDictionary<Guid, Turn> _activeTurns = new();
    private readonly ConditionalWeakTable<HistoryManager, Dictionary<Guid, long[]>> _committed = new();

    internal static Turn? RequestTurn => s_requestTurn.Value;

    internal async Task RunRequestAsync(Guid threadId, Func<Task> run)
    {
        var previous = s_requestTurn.Value;
        s_requestTurn.Value = _activeTurns.GetValueOrDefault(threadId);
        try
        {
            await run().ConfigureAwait(false);
        }
        finally
        {
            s_requestTurn.Value = previous;
        }
    }

    internal Turn? TryBegin(Guid threadId, Guid turnId, HistoryManager history)
    {
        Dispatcher.UIThread.VerifyAccess();
        var turn = new Turn(threadId, turnId, history);
        return _activeTurns.TryAdd(threadId, turn) ? turn : null;
    }

    internal int Finish(Turn turn)
    {
        Dispatcher.UIThread.VerifyAccess();
        turn.IsFinished = true;
        _activeTurns.TryRemove(turn.ThreadId, out _);
        if (turn.Transactions.Count > 0)
            _committed.GetOrCreateValue(turn.History)[turn.Id] = [.. turn.Transactions];
        return turn.Transactions.Count;
    }

    internal bool CanRevert(HistoryManager history, Guid turnId)
    {
        Dispatcher.UIThread.VerifyAccess();
        return _committed.TryGetValue(history, out var turns)
            && turns.TryGetValue(turnId, out var transactions)
            && history.CanUndoTransactions(transactions);
    }

    internal AgentTurnRevertResult Revert(HistoryManager history, Guid turnId)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_committed.TryGetValue(history, out var turns) || !turns.TryGetValue(turnId, out var transactions))
            return AgentTurnRevertResult.Unavailable;
        if (!history.UndoTransactions(transactions))
            return AgentTurnRevertResult.HistoryChanged;

        turns.Remove(turnId);
        return AgentTurnRevertResult.Reverted;
    }

    internal sealed class Turn(Guid threadId, Guid id, HistoryManager history)
    {
        private const int ChangeLimit = 50;

        internal Guid ThreadId { get; } = threadId;
        internal Guid Id { get; } = id;
        internal HistoryManager History { get; } = history;
        internal List<long> Transactions { get; } = [];
        internal List<string> Changes { get; } = [];
        internal bool IsFinished { get; set; }

        internal void TrackEdit(HistoryManager target, Action edit)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (IsFinished || !ReferenceEquals(History, target))
            {
                edit();
                return;
            }

            // An isolated edit can also commit a pending user operation before its own work.
            // Only the isolated transaction belongs to the live MCP request.
            var (subscription, _, _) = target.SubscribeEntries((_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Add
                    && args.NewItems?[0] is HistoryEntry { IsIsolatedEdit: true, TransactionId: long transactionId } entry)
                {
                    Transactions.Add(transactionId);
                    if (Changes.Count < ChangeLimit)
                        Changes.Add(entry.DisplayLabel);
                }
            });
            using (subscription)
                edit();
        }
    }
}
