namespace Beutl.Services;

// Raised when a close abandons itself to keep unsaved edits alive, which only the pre-close save
// does. It derives from InvalidOperationException so existing close-failure handling is unchanged;
// the shutdown path recognizes the type and leaves the application open instead of disposing the
// editors the abort was protecting.
internal sealed class ProjectCloseAbortedException(string message)
    : InvalidOperationException(message);
