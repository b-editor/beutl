using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.Extensibility;

namespace Beutl.HeadlessUITests;

/// <summary>
/// Registers an element source handler the way a package does. Disposing the result removes the
/// package, which retires the handler at once and then waits for its active leases.
/// </summary>
internal static class PackageSourceHandlers
{
    private static int s_nextPackageId = 97_000_000;

    public static IAsyncDisposable Register(
        ExtensionProvider extensions,
        IElementSourceHandler handler,
        ElementSourceHandlerRegistrationMode mode = ElementSourceHandlerRegistrationMode.Add)
    {
        int packageId = Interlocked.Increment(ref s_nextPackageId);
        extensions.AddExtensions(
            packageId,
            [new HandlerExtension(new ElementSourceHandlerRegistration(handler, mode))]);
        return new Package(extensions, packageId);
    }

    private sealed class HandlerExtension(ElementSourceHandlerRegistration registration)
        : ElementSourceHandlerExtension
    {
        public override IReadOnlyCollection<ElementSourceHandlerRegistration> Registrations { get; } =
            [registration];
    }

    private sealed class Package(ExtensionProvider extensions, int packageId) : IAsyncDisposable
    {
        private readonly Lazy<Task> _removal = new(
            () => extensions.RemoveExtensions(packageId).DrainAsync().AsTask());

        public ValueTask DisposeAsync() => new(_removal.Value);
    }
}
