namespace Beutl.Services;

// Raised when a close abandons itself to keep unsaved edits alive. The menu handles this as a
// deliberate cancellation instead of reporting an unexpected operation failure.
internal sealed class ProjectCloseAbortedException(string message)
    : InvalidOperationException(message);
