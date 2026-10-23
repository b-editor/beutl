using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Media.Encoding;

namespace Beutl.HeadlessUITests;

// An .mp4 encoder for the output tests. Its controllers start at defaultSize, and encode with encode or refuse to.
internal class TestEncoder(PixelSize defaultSize = default, Func<TestController, CancellationToken, Task>? encode = null)
    : ControllableEncodingExtension
{
    public override IEnumerable<string> SupportExtensions() => [".mp4"];

    public override EncodingController CreateController(string file) => new TestController(file, defaultSize, encode);
}

internal sealed class TestController(string file, PixelSize defaultSize, Func<TestController, CancellationToken, Task>? encode)
    : EncodingController(file)
{
    public override VideoEncoderSettings VideoSettings { get; } = new() { DestinationSize = { Value = defaultSize } };

    public override AudioEncoderSettings AudioSettings { get; } = new();

    public override ValueTask Encode(IFrameProvider frames, ISampleProvider samples, CancellationToken token)
        => encode is null ? throw new NotSupportedException() : new(encode(this, token));
}
