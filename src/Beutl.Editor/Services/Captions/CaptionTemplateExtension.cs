using Beutl.Extensibility;

namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Contributes element templates to caption workflows independently of caption codecs.
/// </summary>
public abstract class CaptionTemplateExtension : Extension
{
    public abstract IReadOnlyCollection<CaptionTemplateRegistration> Registrations { get; }
}
