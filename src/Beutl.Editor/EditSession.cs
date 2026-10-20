namespace Beutl.Editor;

// Changes made inside Enter() go to this session's own undo entry, apart from the pending transaction.
public sealed class EditSession : IDisposable
{
    private readonly HistoryManager _owner;

    internal EditSession(
        HistoryManager owner, HistoryTransaction transaction, string? name, string? expression, bool yieldsToCommit)
    {
        _owner = owner;
        Transaction = transaction;
        Name = name;
        Expression = expression;
        YieldsToCommit = yieldsToCommit;
    }

    // A commit of edits made outside the session completes it first, so its older entry stays below theirs.
    internal bool YieldsToCommit { get; }

    // Set by a gesture, so an Undo request during it cancels the gesture instead of moving history.
    internal Action? CancelRequested { get; set; }

    internal HistoryTransaction Transaction { get; }

    internal string? Name { get; }

    internal string? Expression { get; }

    public bool IsOpen => _owner.IsSessionOpen(this);

    public IDisposable Enter() => _owner.EnterSession(this);

    public void Complete() => _owner.CompleteSession(this);

    public void Cancel() => _owner.CancelSession(this);

    // An open session is cancelled, so an abandoned gesture leaves nothing behind.
    public void Dispose()
    {
        if (IsOpen)
            Cancel();
    }
}
