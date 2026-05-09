using Beutl.Language;

namespace Beutl.Editor;

public sealed class HistoryEntry
{
    private HistoryEntry(long? transactionId, string? name, string? displayName, int operationCount, DateTime timestamp, bool isInitial)
    {
        TransactionId = transactionId;
        Name = name;
        DisplayName = displayName;
        OperationCount = operationCount;
        Timestamp = timestamp;
        IsInitial = isInitial;
    }

    public long? TransactionId { get; }

    public string? Name { get; }

    public string? DisplayName { get; }

    public int OperationCount { get; }

    public DateTime Timestamp { get; }

    public bool IsInitial { get; }

    public string DisplayLabel => IsInitial
        ? Strings.History_Initial
        : DisplayName ?? Name ?? Strings.History_Unnamed;

    public string TransactionLabel => TransactionId?.ToString() ?? "•";

    internal static HistoryEntry CreateInitial()
    {
        return new HistoryEntry(null, null, null, 0, DateTime.Now, true);
    }

    internal static HistoryEntry FromTransaction(HistoryTransaction transaction)
    {
        return new HistoryEntry(
            transaction.Id,
            transaction.Name,
            transaction.DisplayName,
            transaction.OperationCount,
            DateTime.Now,
            false);
    }
}
