using Beutl.Api.Services;
using Beutl.Extensibility;

namespace Beutl.Editor.Services.Captions;

/// <summary>Contributes template metadata without retaining executable package code.</summary>
public abstract class CaptionTemplateDescriptorExtension : Extension, ILiveUnloadExtension
{
    public abstract IReadOnlyCollection<CaptionTemplateDescriptorRegistration> Registrations { get; }
}

/// <summary>
/// Contributes element factories. Factories can persist package-defined scene graphs, so their
/// packages remain loaded until restart.
/// </summary>
public abstract class CaptionElementFactoryExtension : Extension
{
    public abstract IReadOnlyCollection<CaptionElementFactoryRegistration> Registrations { get; }
}

/// <summary>
/// Contributes data-only placement policies. Active placement calls are drained before unload.
/// </summary>
public abstract class CaptionPlacementPolicyExtension : Extension, ILiveUnloadExtension
{
    public abstract IReadOnlyCollection<CaptionPlacementPolicyRegistration> Registrations { get; }
}
