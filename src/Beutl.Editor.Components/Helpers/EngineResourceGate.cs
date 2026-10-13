using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.Helpers;

/// <summary>
/// Orders a resource's rebuild on its owning dispatcher against readers on other threads, and records its release.
/// </summary>
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
            s_logger.LogWarning(
                releaseFailure, "Releasing a versioned resource behind its last reader failed.");
        }
    }

    /// <summary>
    /// Shuts the gate to new readers and disposes the resource. A release that arrives while a reader is inside
    /// only marks the gate; the outermost reader disposes on its way out.
    /// </summary>
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
