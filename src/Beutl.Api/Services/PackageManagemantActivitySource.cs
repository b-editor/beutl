using System.Diagnostics;

namespace Beutl.Api.Services;

internal static class PackageManagemantActivitySource
{
    public static ActivitySource ActivitySource { get; } = new("Beutl.PackageManagemant", BeutlApplication.Version);
}
