using Beutl.Api.Services;

namespace Beutl.Api;

/// <summary>
/// Explicit composition input for creating an API application. Resource overrides are applied
/// before any resource is resolved, allowing hosts and tests to replace capabilities independently.
/// </summary>
public sealed class BeutlApiApplicationOptions
{
    public BeutlApiApplicationOptions(HttpClient httpClient, IExtensionRegistry extensionRegistry)
    {
        HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ExtensionRegistry = extensionRegistry ?? throw new ArgumentNullException(nameof(extensionRegistry));
    }

    public HttpClient HttpClient { get; }

    public IExtensionRegistry ExtensionRegistry { get; }

    /// <summary>
    /// Gets or initializes the API origin. When omitted, an existing
    /// <see cref="System.Net.Http.HttpClient.BaseAddress"/> is preserved; otherwise the
    /// production Beutl origin is used.
    /// </summary>
    public Uri? ApiBaseUri { get; init; }

    /// <summary>
    /// Gets or initializes the base URI used for account and billing pages.
    /// </summary>
    public Uri? PortalBaseUri { get; init; }

    /// <summary>
    /// Gets or initializes the local authentication-state file name.
    /// </summary>
    public string AuthenticationStateFileName { get; init; } = "user.json";

    public BeutlApiResourceOverrides Resources { get; } = new();
}

public sealed class BeutlApiResourceOverrides
{
    private readonly Dictionary<Type, Func<BeutlApiApplication, IBeutlApiResource>> _factories = [];

    public void Replace<TResource>(Func<BeutlApiApplication, TResource> factory)
        where TResource : class, IBeutlApiResource
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factories[typeof(TResource)] = application => factory(application)
            ?? throw new InvalidOperationException(
                $"The override factory for '{typeof(TResource).FullName}' returned null.");
    }

    internal IEnumerable<KeyValuePair<Type, Func<BeutlApiApplication, IBeutlApiResource>>> Factories
        => _factories;
}
