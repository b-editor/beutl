using Beutl.Api.Services;
using Beutl.Extensibility;

namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Contributes caption descriptors and codec capabilities to caption workflows.
/// </summary>
public abstract class CaptionCodecExtension : Extension, ILiveUnloadExtension
{
    public abstract IReadOnlyCollection<CaptionCodecRegistration> Registrations { get; }
}
