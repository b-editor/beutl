namespace Beutl.Graphics.Rendering;

/// <summary>
/// A recording whose rollback also discards the pending resource registrations it owns.
/// </summary>
internal interface IRenderResourceRecordingScope
{
    bool IsRecording { get; }
}
