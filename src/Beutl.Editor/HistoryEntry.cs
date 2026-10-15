using Beutl.Language;

namespace Beutl.Editor;

public sealed class HistoryEntry
{
    private HistoryEntry(long? transactionId, string? name, string? displayName, DateTime timestamp)
    {
        TransactionId = transactionId;
        Name = name;
        DisplayName = displayName;
        Timestamp = timestamp;
    }

    public DateTime Timestamp { get; }

    public bool IsInitial => TransactionId is null;

    public long? TransactionId { get; }

    public string? Name { get; }

    public string? DisplayName { get; }

    public string DisplayLabel => IsInitial ? Strings.History_Initial : DisplayName ?? Name ?? Strings.History_Unnamed;

    public string TransactionLabel => TransactionId?.ToString() ?? "•";

    internal static HistoryEntry CreateInitial()
    {
        return new HistoryEntry(null, null, null, DateTime.Now);
    }

    internal static HistoryEntry FromTransaction(HistoryTransaction transaction)
    {
        return new HistoryEntry(transaction.Id, transaction.Name, transaction.DisplayName, DateTime.Now);
    }
}
