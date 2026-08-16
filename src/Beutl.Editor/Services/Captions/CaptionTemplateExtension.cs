using Beutl.Api.Services;
using Beutl.Extensibility;

namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Contributes element templates to caption workflows independently of caption codecs.
/// </summary>
public abstract class CaptionTemplateExtension : Extension, ILiveUnloadExtension
{
    public abstract IReadOnlyCollection<CaptionTemplateRegistration> Registrations { get; }
}
