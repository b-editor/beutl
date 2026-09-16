namespace Beutl.Editor.Components.FileBrowserTab;

/// <summary>
/// Storage providers configured for an editor session. Supply this service through
/// <see cref="IEditorContext.GetService"/> to add services without changing file browser navigation.
/// </summary>
public sealed class FileBrowserStorageProviderRegistry
{
    public FileBrowserStorageProviderRegistry(params IFileBrowserStorageProvider[] providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentException.ThrowIfNullOrWhiteSpace(provider.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(provider.DisplayName);
            if (!ids.Add(provider.Id))
                throw new ArgumentException($"Duplicate storage provider ID: {provider.Id}", nameof(providers));
        }
        Providers = Array.AsReadOnly(providers.ToArray());
    }

    public IReadOnlyList<IFileBrowserStorageProvider> Providers { get; }
}
