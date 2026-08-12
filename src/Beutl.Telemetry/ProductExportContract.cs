using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;

namespace Beutl.Services;

/// <summary>
/// The final, fail-closed product trace boundary. This validation is deliberately
/// repeated immediately before OTLP serialization so a first-party friend assembly
/// cannot bypass the public recording helpers and export arbitrary attributes.
/// </summary>
internal static class ProductExportContract
{
    private const double MaximumDurationMilliseconds = 86_400_000;

    private static readonly FrozenSet<string> s_resourceKeys =
    [
        "service.name", "service.version", "beutl.telemetry.stream", "beutl.analytics.schema",
        "beutl.installation.id", "beutl.session.id", "beutl.first_seen_month",
        "beutl.release.channel", "os.type", "process.architecture", "beutl.renderer"
    ];

    private static readonly FrozenSet<string> s_tagKeys =
    [
        "beutl.event.id", "beutl.outcome", ProductAttributeNames.Trigger,
        ProductAttributeNames.ErrorCode, ProductAttributeNames.DurationMilliseconds,
        ProductAttributeNames.FeatureId, ProductAttributeNames.CountBucket,
        ProductAttributeNames.ResolutionBucket, ProductAttributeNames.ProjectSizeBucket
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
    private static readonly Regex s_identifierPattern = new(
        "^[0-9a-f]{32}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex s_monthPattern = new(
        "^[0-9]{4}-(?:0[1-9]|1[0-2])$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static bool IsValid(Activity activity, Resource resource)
    {
        if (!IsValidResource(resource)
            || activity.Source.Name != ProductAnalytics.ActivitySourceName
            || activity.Source.Version != "v1"
            || !string.IsNullOrEmpty(activity.Source.TelemetrySchemaUrl)
            || activity.Source.Tags is { } sourceTags && sourceTags.Any()
            || !ProductEventNames.All.Contains(activity.OperationName)
            || activity.DisplayName != activity.OperationName
            || activity.Kind != ActivityKind.Internal
            || activity.ParentSpanId != default
            || activity.Events.Any()
            || activity.Links.Any()
            || activity.Baggage.Any()
            || !string.IsNullOrEmpty(activity.TraceStateString)
            || !string.IsNullOrEmpty(activity.StatusDescription))
        {
            return false;
        }

        var tags = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> tag in activity.TagObjects)
        {
            if (!s_tagKeys.Contains(tag.Key) || !tags.TryAdd(tag.Key, tag.Value))
            {
                return false;
            }
        }

        if (!TryGetString(tags, "beutl.event.id", out string? eventId)
            || !s_identifierPattern.IsMatch(eventId)
            || !TryGetString(tags, "beutl.outcome", out string? outcome)
            || !ProductOutcomes.All.Contains(outcome)
            || !TryGetDuration(tags, out _))
        {
            return false;
        }

        foreach ((string key, object? value) in tags)
        {
            if (key is "beutl.event.id" or "beutl.outcome" or ProductAttributeNames.DurationMilliseconds)
            {
                continue;
            }

            if (value is not string stringValue
                || stringValue.Length is < 1 or > 256
                || !ProductAttributeNames.IsAllowedValue(key, stringValue))
            {
                return false;
            }
        }

        return outcome == ProductOutcomes.Failed
            ? activity.Status == ActivityStatusCode.Error
            : activity.Status == ActivityStatusCode.Unset;
    }

    internal static Activity[] Filter(in Batch<Activity> batch, Resource resource)
    {
        var accepted = new List<Activity>(checked((int)batch.Count));
        foreach (Activity activity in batch)
        {
            if (IsValid(activity, resource))
            {
                accepted.Add(activity);
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
            && TryGetString(attributes, "beutl.installation.id", out string? installationId)
            && s_identifierPattern.IsMatch(installationId)
            && TryGetString(attributes, "beutl.session.id", out string? sessionId)
            && s_identifierPattern.IsMatch(sessionId)
            && TryGetString(attributes, "beutl.first_seen_month", out string? firstSeenMonth)
            && s_monthPattern.IsMatch(firstSeenMonth)
            && DateOnly.TryParseExact(
                $"{firstSeenMonth}-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _)
            && IsStringIn(attributes, "beutl.release.channel", s_releaseChannels)
            && IsStringIn(attributes, "os.type", s_operatingSystems)
            && IsStringIn(attributes, "process.architecture", s_architectures)
            && IsStringIn(attributes, "beutl.renderer", s_renderers);
    }

    private static bool TryGetDuration(Dictionary<string, object?> tags, out double duration)
    {
        if (!tags.TryGetValue(ProductAttributeNames.DurationMilliseconds, out object? value))
        {
            duration = 0;
            return false;
        }

        duration = value switch
        {
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            long longValue => longValue,
            int intValue => intValue,
            _ => double.NaN
        };
        return double.IsFinite(duration)
            && duration >= 0
            && duration <= MaximumDurationMilliseconds;
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

internal sealed class ProductOtlpTraceExporter(
    OtlpExporterOptions options,
    Func<bool> isEnabled)
    : OtlpTraceExporter(options)
{
    private readonly Func<bool> _isEnabled = isEnabled;

    public override ExportResult Export(in Batch<Activity> batch)
    {
        Activity[] accepted = FilterForExport(batch, ParentProvider.GetResource(), _isEnabled);
        if (accepted.Length == 0)
        {
            return ExportResult.Success;
        }

        var acceptedBatch = new Batch<Activity>(accepted, accepted.Length);
        return base.Export(in acceptedBatch);
    }

    internal static Activity[] FilterForExport(
        in Batch<Activity> batch,
        Resource resource,
        Func<bool> isEnabled)
    {
        return isEnabled()
            ? ProductExportContract.Filter(batch, resource)
            : [];
    }
}
