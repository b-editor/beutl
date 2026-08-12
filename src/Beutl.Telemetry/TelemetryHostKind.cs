namespace Beutl.Services;

/// <summary>
/// Identifies the first-party executable that owns a telemetry provider.
/// </summary>
internal enum TelemetryHostKind
{
    Desktop,
    PackageTools
}
