using Beutl.Extensibility;

namespace Beutl.Api.Services;

/// <summary>
/// Contributes one declarative AI job kind from a package. The host evaluates the descriptor and
/// registration mode when the extension is added, retains the resulting registration for the
/// package lifetime, and drains active descriptor leases before <see cref="Extension.Unload"/>.
/// </summary>
public abstract class AiJobKindExtension : Extension, ILiveUnloadExtension
{
    /// <summary>
    /// Gets the complete behavior descriptor contributed by this extension.
    /// </summary>
    public abstract AiJobKindDescriptor Descriptor { get; }

    /// <summary>
    /// Gets whether this contribution adds a new kind or explicitly replaces the current one.
    /// </summary>
    public abstract AiJobKindRegistrationMode RegistrationMode { get; }
}
