namespace Beutl.PreviewStreaming;

internal enum PreviewTransportEventKind
{
    Error,
    // These values match the callback kinds in native/libdatachannel/preview.cpp.
    Answer = 1,
    Candidate = 2,
    State = 3,
    KeyFrame = 4,
    PlaybackReady = 5
}

internal readonly record struct PreviewTransportEvent(
    PreviewTransportEventKind Kind, string Id, string ConnectionId, bool Hdr, string Value, string Mid = "");
