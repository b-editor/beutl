using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Beutl;

// This is deliberately an internal, BCL-only leaf. Hosting and exporting live in
// Beutl.Telemetry so core projects never acquire an OpenTelemetry SDK dependency.
internal static class ProductAnalytics
{
    internal const string ActivitySourceName = "Beutl.ProductAnalytics";
    internal const string MeterName = "Beutl.Quality";

    // This is the contract version, not the desktop build version. The latter is
    // carried by the service.version resource attribute.
    internal static ActivitySource ActivitySource { get; } = new(ActivitySourceName, "v1");

    internal static Meter Meter { get; } = new(MeterName, "v1");

    internal static Counter<long> ProductEventRejected { get; }
        = Meter.CreateCounter<long>("beutl.product_event.rejected");

    internal static Counter<long> ProductEventRecorded { get; }
        = Meter.CreateCounter<long>("beutl.product_event.recorded");

    internal static Histogram<double> OperationDuration { get; }
        = Meter.CreateHistogram<double>(
            QualityMetricNames.OperationDuration,
            "ms",
            "Duration of a fixed, low-cardinality product operation.");

    internal static Counter<long> OperationTotal { get; }
        = Meter.CreateCounter<long>(
            QualityMetricNames.OperationTotal,
            "{operation}",
            "Number of completed fixed product operations.");

    internal static Counter<long> UncleanSessionTotal { get; }
        = Meter.CreateCounter<long>(
            QualityMetricNames.UncleanSessionTotal,
            "{session}",
            "Number of starts following an unclean desktop session.");

    internal static void RecordQualityOperation(string operation, string outcome, double durationMilliseconds)
    {
        if (!QualityOperations.All.Contains(operation)
            || !QualityOutcomes.All.Contains(outcome)
            || !double.IsFinite(durationMilliseconds)
            || durationMilliseconds < 0
            || durationMilliseconds > 86_400_000)
        {
            return;
        }

        TagList tags = default;
        tags.Add(QualityAttributeNames.Operation, operation);
        tags.Add(QualityAttributeNames.Outcome, outcome);
        OperationDuration.Record(durationMilliseconds, in tags);
        OperationTotal.Add(1, in tags);
    }

    internal static void RecordUncleanSession()
    {
        UncleanSessionTotal.Add(1);
    }
}

internal static class QualityMetricNames
{
    internal const string OperationDuration = "beutl.quality.operation.duration";
    internal const string OperationTotal = "beutl.quality.operation.total";
    internal const string UncleanSessionTotal = "beutl.quality.unclean_session.total";

    internal static readonly FrozenSet<string> All =
    [OperationDuration, OperationTotal, UncleanSessionTotal];
}

internal static class QualityAttributeNames
{
    internal const string Operation = "beutl.operation";
    internal const string Outcome = "beutl.outcome";
}

internal static class QualityOperations
{
    internal static readonly FrozenSet<string> All =
    [
        "app.session.start",
        "project.open",
        "preview.first_frame",
        "preview.playback_summary",
        "media.export"
    ];
}

internal static class QualityOutcomes
{
    internal static readonly FrozenSet<string> All =
    ["success", "partial", "failed", "cancelled", "blocked", "queued"];
}
