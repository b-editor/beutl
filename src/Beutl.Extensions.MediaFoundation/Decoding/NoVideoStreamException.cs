#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

// Thrown when a file has no video stream at all, as opposed to a video stream that
// failed to initialize. Callers use this to fall back to audio-only handling while
// still letting genuine video initialization failures propagate.
internal sealed class NoVideoStreamException(string message) : Exception(message);
