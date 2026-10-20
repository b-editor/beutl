using System.Diagnostics;

namespace Beutl;

// DEBUG builds report writes to the open project's objects made off the thread that owns the document (#2873).
internal static class DocumentWriteCheck
{
    // Null turns the check off; the headless UI tests set both.
    internal static Func<bool>? IsOwnerThread { get; set; }

    internal static Action<string>? Report { get; set; }

    [Conditional("DEBUG")]
    internal static void Verify(object? target, string? member)
    {
        if (IsOwnerThread is not { } isOwnerThread
            || target is not IHierarchical { HierarchicalRoot: { } root }
            || !ReferenceEquals(root, BeutlApplication.Current)
            || isOwnerThread())
        {
            return;
        }

        Report?.Invoke($"{target.GetType().Name}.{member} was written off the UI thread.");
    }
}
