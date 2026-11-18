namespace Beutl.Extensibility;

/// <summary>The host that is loading or unloading an extension.</summary>
public interface IExtensionContext
{
    IServiceProvider Services { get; }
}
