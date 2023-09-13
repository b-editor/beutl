#pragma warning disable CS0436

using System.Runtime.CompilerServices;

namespace Beutl.Services;

internal static class Telemetry
{
    public static ActivitySource ActivitySource { get; } = new("Beutl.Application", GitVersionInformation.SemVer);

    public static Activity? StartActivity([CallerMemberName] string name = "", ActivityKind kind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(name, kind);
    }
}
