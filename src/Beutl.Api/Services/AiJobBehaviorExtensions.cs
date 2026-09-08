using Beutl.Extensibility;

namespace Beutl.Api.Services;

/// <summary>Contributes status resolution independently of refresh and retry behavior.</summary>
public abstract class AiJobStatusResolverExtension : Extension, ILiveUnloadExtension
{
    public abstract IReadOnlyCollection<AiJobStatusResolverRegistration> Registrations { get; }
}

/// <summary>Contributes polling refresh independently of status and retry behavior.</summary>
public abstract class AiJobRefreshHandlerExtension : Extension, ILiveUnloadExtension
{
    public abstract IReadOnlyCollection<AiJobRefreshHandlerRegistration> Registrations { get; }
}

/// <summary>Contributes retry behavior independently of status and refresh behavior.</summary>
public abstract class AiJobRetryHandlerExtension : Extension, ILiveUnloadExtension
{
    public abstract IReadOnlyCollection<AiJobRetryHandlerRegistration> Registrations { get; }
}
