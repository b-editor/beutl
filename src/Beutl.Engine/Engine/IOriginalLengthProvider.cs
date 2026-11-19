using Beutl.Media.Decoding;
namespace Beutl.Engine;

public interface IOriginalDurationProvider
{
    bool HasOriginalDuration();

    bool TryGetOriginalDuration(out MediaTime timeSpan, DecoderRegistry? decoders = null);
}
