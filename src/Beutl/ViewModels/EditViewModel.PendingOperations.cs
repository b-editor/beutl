using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Dispatcher = Avalonia.Threading.Dispatcher;

namespace Beutl.ViewModels;

public sealed partial class EditViewModel
{
    // DEBUG builds name the edits that pointer or key input leaves uncommitted, which the next commit would take (#2874).
    [Conditional("DEBUG")]
    private void WarnAboutEditsPendingAfterInput()
    {
        InputElement.PointerReleasedEvent.RouteFinished
            .Merge(InputElement.KeyUpEvent.RouteFinished)
            .Subscribe(_ => Dispatcher.UIThread.Post(WarnAboutPendingEdits, DispatcherPriority.Background))
            .DisposeWith(_disposables);
    }

    private void WarnAboutPendingEdits()
    {
        // A nudge commits on its own timer.
        if (_disposed || _elementNudgeService?.IsPending == true)
            return;

        if (HistoryManager.DescribeNewPendingOperations() is { } operations)
            _logger.LogWarning("Input ended with edits that nothing committed: {Operations}", operations);
    }
}
