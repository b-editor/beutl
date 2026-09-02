using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.Helpers;

/// <summary>
/// Orders one subscription's resource rebuild against readers on other threads.
/// </summary>
/// <remarks>
/// A versioned resource is rebuilt in place, so the dispatcher that owns it is a writer every other thread
/// races: <c>CompareAndUpdateList</c> replaces the entries of the lists the resource owns and disposes the
/// ones it drops, and <c>CompareAndUpdate</c> overwrites its values. Holding this while rebuilding and while
/// reading is what keeps a reader from landing midway through one. The release runs here too, so a handle
/// that outlives its subscription can tell that the resource behind it is gone.
/// </remarks>
internal sealed class EngineResourceGate
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(EngineResourceGate));
    private Action? _release;

    public object SyncRoot { get; } = new();

    /// <summary>Whether the resource has been released. Read and written under <see cref="SyncRoot"/>.</summary>
    public bool IsReleased { get; private set; }

    /// <summary>How many readers are inside the gate. Read and written under <see cref="SyncRoot"/>.</summary>
    public int ActiveReaders { get; private set; }

    /// <summary>
    /// Whether a release arrived while a reader was inside and left the disposal to it. Read and written
    /// under <see cref="SyncRoot"/>.
    /// </summary>
    public bool ReleaseDeferred { get; private set; }

    /// <summary>Records the teardown that disposes the resource, once, as the subscription is created.</summary>
    public void SetRelease(Action release) => _release = release;

    /// <summary>Records a reader as inside the gate. Call under <see cref="SyncRoot"/>.</summary>
    public void EnterRead() => ActiveReaders++;

    /// <summary>
    /// Records a reader as out, running a release that was waiting for it. Call under <see cref="SyncRoot"/>.
    /// </summary>
    public void ExitRead()
    {
        if (--ActiveReaders > 0 || !ReleaseDeferred)
            return;

        ReleaseDeferred = false;
        try
        {
            _release?.Invoke();
        }
        catch (Exception releaseFailure)
        {
            // Whoever asked for the release has long since returned, so there is nobody left to report this
            // to, and this runs on the reader's thread - the render thread in the case a release defers for,
            // whose loop installs no unhandled-exception handler.
            s_logger.LogWarning(
                releaseFailure, "Releasing a versioned resource behind its last reader failed.");
        }
    }

    /// <summary>
    /// Shuts the gate to new readers and disposes the resource, or leaves the disposal to the reader that is
    /// already inside.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Marking the gate released before the teardown is what leaves an already-published handle reporting
    /// itself empty rather than reaching a disposed resource.
    /// </para>
    /// <para>
    /// A release reaches this from inside a read whenever the two meet on the owning dispatcher's thread:
    /// that dispatcher runs a cleanup requested from its own thread inline rather than queueing it, so a
    /// subscriber disposing its subscription from within a read walks back in through the re-entrant lock it
    /// is still holding. Disposing there would free the resource out from under the reader above, so the
    /// inner frame only shuts the door and the outermost reader finishes the job on its way out.
    /// </para>
    /// </remarks>
    public void Release()
    {
        lock (SyncRoot)
        {
            IsReleased = true;
            if (ActiveReaders > 0)
            {
                ReleaseDeferred = true;
                return;
            }

            _release?.Invoke();
        }
    }
}
