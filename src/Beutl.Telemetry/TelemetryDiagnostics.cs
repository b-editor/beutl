using System.Collections.Frozen;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

/// <summary>
/// The only supported path for a remote diagnostic log. It intentionally accepts
/// fixed component/code/outcome values rather than messages, paths, or exceptions.
/// </summary>
internal static class TelemetryDiagnostics
{
    internal const string CategoryName = "Beutl.SafeDiagnostics";
    private static readonly FrozenSet<string> s_components =
    ["app", "project", "preview", "export", "package", "extension", "agent", "telemetry"];

    internal static void Report(string component, string code, string outcome)
    {
        if (!IsAllowed(component, code, outcome))
        {
            return;
        }

        Telemetry.Instance?.ReportDiagnostic(component, code, outcome);
    }

    internal static bool IsAllowed(string component, string code, string outcome)
    {
        return s_components.Contains(component)
            && ProductAttributeNames.IsAllowedValue(ProductAttributeNames.ErrorCode, code)
            && ProductOutcomes.All.Contains(outcome);
    }
}
