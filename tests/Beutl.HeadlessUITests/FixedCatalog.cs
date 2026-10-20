using Beutl.Api.Services;

namespace Beutl.HeadlessUITests;

// A model catalog service that always returns the same catalog, for the AI tests.
internal sealed class FixedCatalog(AiModelCatalog catalog) : IAiModelCatalogService
{
    public Task<AiModelCatalog> GetAsync(CancellationToken cancellationToken) => Task.FromResult(catalog);

    public void Invalidate()
    {
    }
}
