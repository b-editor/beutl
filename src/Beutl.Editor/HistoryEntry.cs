using Beutl.Language;

namespace Beutl.Editor;

public abstract class HistoryEntry
{
    private protected HistoryEntry(DateTime timestamp)
    {
        Timestamp = timestamp;
    }

    public DateTime Timestamp { get; }

    public abstract bool IsInitial { get; }

    public abstract long? TransactionId { get; }

    public abstract string? Name { get; }

    public abstract string? DisplayName { get; }

    public abstract int OperationCount { get; }

    public abstract string DisplayLabel { get; }

    public abstract string TransactionLabel { get; }

    internal static HistoryEntry CreateInitial()
    {
        return new InitialHistoryEntry(DateTime.UtcNow);
    }

    internal static HistoryEntry FromTransaction(HistoryTransaction transaction)
    {
        return new TransactionHistoryEntry(
            transaction.Id,
            transaction.Name,
            transaction.DisplayName,
            transaction.OperationCount,
            DateTime.UtcNow);
    }
}

public sealed class InitialHistoryEntry : HistoryEntry
{
    internal InitialHistoryEntry(DateTime timestamp) : base(timestamp)
    {
    }

    public override bool IsInitial => true;

    public override long? TransactionId => null;

    public override string? Name => null;

    public override string? DisplayName => null;

    public override int OperationCount => 0;

    public override string DisplayLabel => Strings.History_Initial;

    public override string TransactionLabel => "•";
}

public sealed class TransactionHistoryEntry : HistoryEntry
{
    internal TransactionHistoryEntry(
        long transactionId,
        string? name,
        string? displayName,
        int operationCount,
        DateTime timestamp) : base(timestamp)
    {
        TransactionIdValue = transactionId;
        Name = name;
        DisplayName = displayName;
        OperationCount = operationCount;
    }

    public override bool IsInitial => false;

    public override long? TransactionId => TransactionIdValue;

    public long TransactionIdValue { get; }

    public override string? Name { get; }

    public override string? DisplayName { get; }

    public override int OperationCount { get; }

    public override string DisplayLabel => DisplayName ?? Name ?? Strings.History_Unnamed;

    public override string TransactionLabel => TransactionIdValue.ToString();
}
