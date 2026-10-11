using System.Diagnostics;

namespace Beutl.Api.Services;

internal static class PackageManagementActivitySource
{
    // Telemetry subscribes to this name when the user consents to package management traces.
    internal const string Name = "Beutl.PackageManagement";

    public static ActivitySource ActivitySource { get; } = new(Name, BeutlApplication.Version);
}
