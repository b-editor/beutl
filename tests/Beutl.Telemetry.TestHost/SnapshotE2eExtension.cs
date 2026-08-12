using Beutl.Extensibility;

namespace Beutl.Telemetry.TestHost;

/// <summary>
/// Minimal package payload used only by the end-to-end trusted-snapshot test.
/// </summary>
[Export]
public sealed class SnapshotE2eExtension : Extension;
