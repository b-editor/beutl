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

    // Every encoder that supports the container, in registration order, so a caller can fall back
    // to the next one when the preferred encoder's runtime (e.g. FFmpeg's native libs) is missing.
    public IReadOnlyList<ControllableEncodingExtension> FindAllForOutput(string outputPath)
    {
        return Encoders.Where(encoder => encoder.IsSupported(outputPath)).ToArray();
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
