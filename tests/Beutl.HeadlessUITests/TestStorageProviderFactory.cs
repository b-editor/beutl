using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Platform.Storage;

namespace Beutl.HeadlessUITests;

// Opt individual test windows into mocked native pickers; all other windows use the usual fallback.
internal sealed class TestStorageProviderFactory : IStorageProviderFactory
{
    private static readonly ConditionalWeakTable<TopLevel, IStorageProvider> s_providers = new();

    public static void SetProvider(TopLevel window, IStorageProvider provider)
        => s_providers.Add(window, provider);

    // TopLevel explicitly falls back to the platform provider when the factory returns null.
    public IStorageProvider CreateProvider(TopLevel topLevel)
        => s_providers.TryGetValue(topLevel, out IStorageProvider? provider) ? provider : null!;
}
