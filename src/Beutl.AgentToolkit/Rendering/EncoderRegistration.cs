using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Encoding;

namespace Beutl.AgentToolkit.Rendering;

public sealed class EncoderRegistration
{
    private readonly Lazy<IReadOnlyList<ControllableEncodingExtension>> _encoders = new(Register);

    public IReadOnlyList<ControllableEncodingExtension> Encoders => _encoders.Value;

    public ControllableEncodingExtension? FindForOutput(string outputPath)
    {
        return Encoders.FirstOrDefault(encoder => encoder.IsSupported(outputPath));
    }

    private static IReadOnlyList<ControllableEncodingExtension> Register()
    {
        var encoders = new List<ControllableEncodingExtension>
        {
            new FFmpegHeadlessEncodingExtension()
        };

        if (OperatingSystem.IsMacOS())
        {
            encoders.Add(new Beutl.Extensions.AVFoundation.Encoding.AVFEncodingExtension());
        }

        foreach (ControllableEncodingExtension encoder in encoders)
        {
            encoder.Load();
        }

        return encoders;
    }
}
