namespace Beutl.Graphics.Rendering;

/// <summary>
/// A recording whose rollback also discards the pending resource registrations it owns.
/// </summary>
/// <remarks>
/// A pending registration is readable only while the recording that owns it is still in flight: only then
/// does a rollback of the registration take the reader's own recorded work with it.
/// </remarks>
internal interface IRenderResourceRecordingScope
{
    bool IsRecording { get; }
}
