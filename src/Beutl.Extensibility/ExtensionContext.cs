namespace Beutl.Extensibility;

public sealed class ExtensionContext(IServiceProvider services) : IExtensionContext
{
    public IServiceProvider Services { get; } = services ?? throw new ArgumentNullException(nameof(services));
}
