using Beutl.Api.Services;
using Beutl.Extensibility;

namespace Beutl.Editor.Services.Captions;

/// <summary>Contributes caption format metadata independently from executable codecs.</summary>
public abstract class CaptionCodecDescriptorExtension : Extension, ILiveUnloadExtension
{
    public abstract IReadOnlyCollection<CaptionCodecDescriptorRegistration> Registrations { get; }
}

/// <summary>Contributes caption decoders whose active calls drain before live unload.</summary>
public abstract class CaptionDecoderExtension : Extension, ILiveUnloadExtension
{
    public abstract IReadOnlyCollection<CaptionDecoderRegistration> Registrations { get; }
}

/// <summary>Contributes caption encoders whose active calls drain before live unload.</summary>
public abstract class CaptionEncoderExtension : Extension, ILiveUnloadExtension
{
    public abstract IReadOnlyCollection<CaptionEncoderRegistration> Registrations { get; }
}
