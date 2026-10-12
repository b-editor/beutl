using Beutl.Extensibility;
using Beutl.Media.Encoding;

namespace Beutl.AgentToolkit.Tests.Helpers;

// An encoder for one extension whose Encode body the test supplies.
internal sealed class DelegateEncoder(
    string extension,
    Func<EncodingController, IFrameProvider, ISampleProvider, CancellationToken, Task> encode)
    : ControllableEncodingExtension
{
    public EncodingController? Controller { get; private set; }

    public override IEnumerable<string> SupportExtensions() => [extension];

    public override EncodingController CreateController(string file) => Controller = new DelegateController(file, encode);

    private sealed class DelegateController(
        string file,
        Func<EncodingController, IFrameProvider, ISampleProvider, CancellationToken, Task> encode)
        : EncodingController(file)
    {
        public override VideoEncoderSettings VideoSettings { get; } = new();

        public override AudioEncoderSettings AudioSettings { get; } = new();

        public override ValueTask Encode(IFrameProvider frames, ISampleProvider samples, CancellationToken token)
            => new(encode(this, frames, samples, token));
    }
}
