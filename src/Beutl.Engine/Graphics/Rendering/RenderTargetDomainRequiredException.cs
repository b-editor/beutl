namespace Beutl.Graphics.Rendering;

/// <summary>
/// The recorded render graph requires a finite owning target domain that the current request did not provide.
/// </summary>
public sealed class RenderTargetDomainRequiredException : InvalidOperationException
{
    /// <summary>Initializes the exception with the domain requirement that could not be satisfied.</summary>
    public RenderTargetDomainRequiredException(string message)
        : base(message)
    {
    }
}
