namespace Beutl.Editor;

/// <summary>
/// A history operation failed with an unknown partial change. Undo and redo history
/// was reset; the current model can still be edited and saved.
/// </summary>
public sealed class HistoryResetException(Exception innerException)
    : Exception("The history operation partially failed. Undo and redo history was reset; editing and saving can continue.", innerException);
