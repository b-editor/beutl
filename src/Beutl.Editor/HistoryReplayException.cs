namespace Beutl.Editor;

/// <summary>
/// A history operation failed with an unknown partial change. Existing history is
/// preserved but cannot be replayed across the failure. New edits can still be
/// recorded, undone, redone, and saved.
/// </summary>
public sealed class HistoryReplayException(Exception innerException)
    : Exception("The history operation partially failed. History was preserved, but replay across the failure is blocked; editing and saving can continue.", innerException);
