namespace Beutl.Services;

// Thrown when the open project or its version-control backend went away under an operation. Callers
// that report "the project changed" catch this type alone, so a failure from Git or from the
// operation itself still reaches the user with its own reason.
internal sealed class VersionControlLifecycleUnavailableException(string message)
    : InvalidOperationException(message);
