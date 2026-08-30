namespace Beutl.ViewModels.Dialogs;

/// <summary>
/// The result of an AI dialog save picker. The picker and the write are kept as
/// separate steps so identity fencing can reject a late destination before it
/// opens or mutates that destination.
/// </summary>
internal sealed record AiSaveFileDestination(
    string Path,
    Func<CancellationToken, Task<Stream>> OpenWriteAsync);
