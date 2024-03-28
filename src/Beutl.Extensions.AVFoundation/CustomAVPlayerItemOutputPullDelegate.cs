using MonoMac.AVFoundation;

namespace Beutl.Extensions.AVFoundation.Decoding;

internal sealed class CustomAVPlayerItemOutputPullDelegate : AVPlayerItemOutputPullDelegate
{
    public override void OutputMediaDataWillChange(AVPlayerItemOutput sender)
    {
        base.OutputMediaDataWillChange(sender);
    }

    public override void OutputSequenceWasFlushed(AVPlayerItemOutput output)
    {
        base.OutputSequenceWasFlushed(output);
    }
}
