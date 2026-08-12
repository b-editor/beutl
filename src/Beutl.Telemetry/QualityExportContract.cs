using System.Collections.Frozen;
using System.Text.RegularExpressions;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace Beutl.Services;

/// <summary>
/// Final fail-closed boundary for aggregate-only quality metrics.
/// </summary>
internal static class QualityExportContract
{
    private static readonly FrozenSet<string> s_resourceKeys =
    [
        "service.name", "service.version", "beutl.telemetry.stream", "beutl.analytics.schema",
        "beutl.release.channel", "os.type", "process.architecture", "beutl.renderer"
    ];

    private static readonly FrozenSet<string> s_serviceNames = ["beutl.desktop", "beutl.package-tools"];
    private static readonly FrozenSet<string> s_releaseChannels = ["stable", "beta", "nightly", "development"];
    private static readonly FrozenSet<string> s_operatingSystems = ["windows", "linux", "macos", "other"];
    private static readonly FrozenSet<string> s_architectures = ["x86", "x64", "arm", "arm64", "wasm"];
    private static readonly FrozenSet<string> s_renderers =
    ["unknown", "software", "opengl", "vulkan", "metal", "direct3d11", "skia"];

    private static readonly Regex s_versionPattern = new(
        "^[0-9]{1,4}\\.[0-9]{1,4}\\.[0-9]{1,4}(?:[-+][0-9A-Za-z][0-9A-Za-z.-]{0,47})?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static ExplicitBucketHistogramConfiguration CreateDurationHistogramConfiguration()
    {
        return new ExplicitBucketHistogramConfiguration
        {
            // A null boundary list selects the OpenTelemetry standard explicit buckets.
            Boundaries = null,
            RecordMinMax = true
        };
    }

    internal static bool IsValid(Metric metric, Resource resource)
    {
        if (!IsValidResource(resource)
            || metric.MeterName != ProductAnalytics.MeterName
            || metric.MeterVersion != "v1"
            || !string.IsNullOrEmpty(metric.MeterSchemaUrl)
            || metric.MeterTags is { } meterTags && meterTags.Any()
            || metric.Temporality != AggregationTemporality.Delta)
        {
            return false;
        }

        bool isOperation;
        if (metric.Name == QualityMetricNames.OperationDuration)
        {
            isOperation = true;
            if (metric.MetricType != MetricType.Histogram
                || metric.Unit != "ms"
                || metric.Description != "Duration of a fixed, low-cardinality product operation.")
            {
                return false;
            }
        }
        else if (metric.Name == QualityMetricNames.OperationTotal)
        {
            isOperation = true;
            if (metric.MetricType != MetricType.LongSum
                || metric.Unit != "{operation}"
                || metric.Description != "Number of completed fixed product operations.")
            {
                return false;
            }
        }
        else if (metric.Name == QualityMetricNames.UncleanSessionTotal)
        {
            isOperation = false;
            if (metric.MetricType != MetricType.LongSum
                || metric.Unit != "{session}"
                || metric.Description != "Number of starts following an unclean desktop session.")
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        bool hasPoint = false;
        foreach (ref readonly MetricPoint point in metric.GetMetricPoints())
        {
            hasPoint = true;
            var tags = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> tag in point.Tags)
            {
                if (!tags.TryAdd(tag.Key, tag.Value))
                {
                    return false;
                }
            }

            if (isOperation)
            {
                if (tags.Count != 2
                    || !TryGetString(tags, QualityAttributeNames.Operation, out string? operation)
                    || !QualityOperations.All.Contains(operation)
                    || !TryGetString(tags, QualityAttributeNames.Outcome, out string? outcome)
                    || !QualityOutcomes.All.Contains(outcome))
                {
                    return false;
                }
            }
            else if (tags.Count != 0)
            {
                return false;
            }

            if (metric.Name == QualityMetricNames.OperationDuration)
            {
                long count = point.GetHistogramCount();
                double sum = point.GetHistogramSum();
                if (count <= 0
                    || !double.IsFinite(sum)
                    || sum < 0
                    || sum > 86_400_000d * count
                    || point.TryGetHistogramMinMaxValues(out double min, out double max)
                    && (!double.IsFinite(min)
                        || !double.IsFinite(max)
                        || min < 0
                        || min > max
                        || max > 86_400_000d))
                {
                    return false;
                }
            }
            else if (point.GetSumLong() <= 0)
            {
                return false;
            }
        }

        return hasPoint;
    }

    internal static Metric[] Filter(in Batch<Metric> batch, Resource resource)
    {
        var accepted = new List<Metric>(checked((int)batch.Count));
        foreach (Metric metric in batch)
        {
            if (IsValid(metric, resource))
            {
                accepted.Add(metric);
            }
        }

        return [.. accepted];
    }

    internal static bool IsValidResource(Resource resource)
    {
        if (resource.SchemaUrl is not null)
        {
            return false;
        }

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object> attribute in resource.Attributes)
        {
            if (!s_resourceKeys.Contains(attribute.Key)
                || !attributes.TryAdd(attribute.Key, attribute.Value))
            {
                return false;
            }
        }

        return attributes.Count == s_resourceKeys.Count
            && IsStringIn(attributes, "service.name", s_serviceNames)
            && TryGetString(attributes, "service.version", out string? version)
            && version.Length <= 64
            && s_versionPattern.IsMatch(version)
            && IsExactString(attributes, "beutl.telemetry.stream", "product")
            && IsExactString(attributes, "beutl.analytics.schema", "v1")
            && IsStringIn(attributes, "beutl.release.channel", s_releaseChannels)
            && IsStringIn(attributes, "os.type", s_operatingSystems)
            && IsStringIn(attributes, "process.architecture", s_architectures)
            && IsStringIn(attributes, "beutl.renderer", s_renderers);
    }

    private static bool TryGetString(
        Dictionary<string, object?> attributes,
        string key,
        out string value)
    {
        if (attributes.TryGetValue(key, out object? candidate) && candidate is string stringValue)
        {
            value = stringValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool IsExactString(
        Dictionary<string, object?> attributes,
        string key,
        string expected)
    {
        return TryGetString(attributes, key, out string? value)
            && value == expected;
    }

    private static bool IsStringIn(
        Dictionary<string, object?> attributes,
        string key,
        FrozenSet<string> allowed)
    {
        return TryGetString(attributes, key, out string? value)
            && allowed.Contains(value);
    }
}

internal sealed class QualityOtlpMetricExporter(
    OtlpExporterOptions options,
    Func<bool> isEnabled)
    : OtlpMetricExporter(options)
{
    private readonly Func<bool> _isEnabled = isEnabled;

    public override ExportResult Export(in Batch<Metric> batch)
    {
        if (!_isEnabled())
        {
            return ExportResult.Success;
        }

        Metric[] accepted = QualityExportContract.Filter(batch, ParentProvider.GetResource());
        if (accepted.Length == 0)
        {
            return ExportResult.Success;
        }

        var acceptedBatch = new Batch<Metric>(accepted, accepted.Length);
        return base.Export(in acceptedBatch);
    }
}
