using System.Collections.Concurrent;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Records the validation errors the Vulkan debug messenger reports, so something other than a log reader
/// can act on them.
/// </summary>
/// <remarks>
/// Collects validation errors so a validation-enabled run can fail on them; nothing is recorded unless validation
/// is enabled. Only the test gates read it back.
/// </remarks>
internal sealed class VulkanValidationErrorLog
{
    // A single mistake inside a loop can report thousands of times. The count stays exact; only the
    // retained text is bounded, because its purpose is to name the failure, not to archive it.
    private const int MaxRetainedMessages = 32;

    private readonly ConcurrentQueue<string> _messages = new();
    private int _count;

    /// <summary>Gets the log the debug messenger writes to.</summary>
    public static VulkanValidationErrorLog Shared { get; } = new();

    /// <summary>Gets how many validation errors have been recorded.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>Gets the retained messages, oldest first.</summary>
    /// <remarks>Fewer than <see cref="Count"/> once more errors have arrived than are retained.</remarks>
    public IReadOnlyList<string> Messages => [.. _messages];

    /// <summary>Records one validation error.</summary>
    /// <remarks>
    /// <see cref="Shared"/> is written from the unmanaged debug-messenger callback, on whichever thread the
    /// layer reported on.
    /// </remarks>
    public void Record(string? message)
    {
        Interlocked.Increment(ref _count);
        _messages.Enqueue(string.IsNullOrEmpty(message) ? "<the layer supplied no message text>" : message);
        while (_messages.Count > MaxRetainedMessages && _messages.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// Describes the errors recorded since <paramref name="previousCount"/>, or an empty string when none
    /// were.
    /// </summary>
    /// <param name="previousCount">A <see cref="Count"/> value read before the work being attributed.</param>
    public string DescribeSince(int previousCount)
    {
        int added = Count - previousCount;
        return added <= 0 ? string.Empty : Format(added, Messages);
    }

    internal static string Format(int added, IReadOnlyList<string> retained)
    {
        IEnumerable<string> relevant = added >= retained.Count
            ? retained
            : retained.Skip(retained.Count - added);
        string body = string.Join(Environment.NewLine, relevant.Select(static item => "  " + item));
        string header = $"{added} Vulkan validation error(s) were reported";
        if (added > retained.Count)
            header += $" (the {retained.Count} most recent are shown)";

        return $"{header}:{Environment.NewLine}{body}";
    }
}
