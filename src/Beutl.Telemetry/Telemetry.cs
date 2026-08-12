using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Beutl.Configuration;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace Beutl.Services;

/// <summary>
/// Owns the desktop telemetry lifecycle. Product analytics and operational
/// diagnostics use separate providers and resources so pseudonymous identities
/// cannot leak into metrics, logs, or general traces.
/// </summary>
internal sealed class Telemetry : IDisposable
{
    internal const string UncleanSessionMarkerFileName = TelemetryUncleanSessionMarker.FileName;
    internal const string SessionIdEnvironmentVariable = "BEUTL_TELEMETRY_SESSION_ID";
    private const string ProductSchemaVersion = "v1";
    private static readonly object s_instanceGate = new();
    private static readonly object s_lifecycleGate = new();
    private static readonly ConditionalWeakTable<Type, TrustedFeature> s_trustedFeatures = new();
    private static readonly Dictionary<Type, TrustedFeature> s_builtinFeatures = [];
    private static readonly object s_builtinFeatureGate = new();
    private static readonly TimeSpan s_productSummaryInterval = TimeSpan.FromMinutes(5);
    private readonly object _gate = new();
    private readonly TelemetryConfig _configuration;
    private readonly TelemetryIdentityStore _identityStore;
    private readonly string _sessionId;
    private readonly string _serviceName;
    private readonly TelemetryHostKind _hostKind;
    private readonly ProductSummaryBuffer _productSummaries;
    private readonly ILoggerFactory _localLoggerFactory;
    private TracerProvider? _operationalTracerProvider;
    private TracerProvider? _productTracerProvider;
    private MeterProvider? _meterProvider;
    private ILoggerFactory? _diagnosticLoggerFactory;
    private int _usageAnalyticsEnabled;
    private int _uncleanSessionRecorded;
    private string? _sessionStartedInstallationId;
    private string? _activeInstallationId;
    private bool _disposed;

    static Telemetry()
    {
        PackageAnalytics.Configure(new PackageAnalyticsHandlers(
            StartExtensionLoadAnalytics,
            ExtensionManageTelemetry.RecordQueued,
            RegisterTrustedFeature,
            UnregisterTrustedFeature,
            RecordExtensionLoaded));
    }

    private Telemetry(string? sessionId, TelemetryHostKind hostKind)
    {
        _configuration = GlobalConfiguration.Instance.TelemetryConfig;
        _identityStore = new TelemetryIdentityStore();
        _sessionId = IsSessionId(sessionId) ? sessionId! : Guid.NewGuid().ToString("N");
        _hostKind = hostKind;
        _serviceName = GetServiceName(hostKind);
        _localLoggerFactory = SetupLocalLogger(_hostKind);
        _configuration.ConfigurationChanged += OnConfigurationChanged;
        RebuildProviders();
        _productSummaries = new ProductSummaryBuffer(
            TimeProvider.System,
            s_productSummaryInterval,
            FlushProductSummaries);
    }

    internal static Telemetry? Instance { get; private set; }

    /// <summary>Process scoped random session identifier, safe only for product telemetry correlation.</summary>
    internal string SessionId => _sessionId;

    internal string? ActiveInstallationId => _activeInstallationId;

    internal bool UsageAnalyticsEnabled => IsUsageAnalyticsEnabled();

    internal static ActivitySource ApplicationActivitySource { get; } = new("Beutl.Application", BeutlApplication.Version);

    internal static IDisposable GetDisposable(
        string? sessionId = null,
        TelemetryHostKind hostKind = TelemetryHostKind.Desktop)
    {
        // Never hold s_instanceGate while disposing an old instance: Dispose takes
        // its own provider lock before touching s_instanceGate, so that ordering
        // would deadlock with an independently disposed host instance.
        lock (s_lifecycleGate)
        {
            Telemetry? previous;
            lock (s_instanceGate)
            {
                previous = Instance;
                Instance = null;
            }

            previous?.Dispose();

            var telemetry = new Telemetry(sessionId, hostKind);
            lock (s_instanceGate)
            {
                Instance = telemetry;
            }

            return telemetry;
        }
    }

    internal static Activity? StartActivity([CallerMemberName] string name = "", ActivityKind kind = ActivityKind.Internal)
    {
        return ApplicationActivitySource.StartActivity(name, kind);
    }

    internal static ProductOperation StartProductOperation(
        string name,
        IEnumerable<KeyValuePair<string, string>>? attributes = null)
    {
        return Instance?.StartProductOperationCore(name, attributes) ?? new ProductOperation(null, null);
    }

    internal static void RecordProductEvent(
        string name,
        string outcome,
        IEnumerable<KeyValuePair<string, string>>? attributes = null,
        string? errorCode = null)
    {
        Instance?.RecordProductEventCore(name, outcome, attributes, errorCode);
    }

    internal static ProductSummaryOperation StartProductSummary(
        string name,
        string trigger,
        string? featureId = null)
    {
        return Instance?.StartProductSummaryCore(name, trigger, featureId)
            ?? new ProductSummaryOperation(null);
    }

    internal static bool IsConsentConfigured(TelemetryConfig config)
    {
        return config.Beutl_Api_Client.HasValue
            && config.Beutl_Application.HasValue
            && config.Beutl_PackageManagement.HasValue
            && config.Beutl_Logging.HasValue
            && config.UsageAnalytics.HasValue;
    }

    internal static string? GetSessionIdFromEnvironment()
    {
        string? value = Environment.GetEnvironmentVariable(SessionIdEnvironmentVariable);
        return IsSessionId(value) ? value : null;
    }

    internal static void ResetUsageIdentity()
    {
        Instance?.ResetUsageIdentityCore();
    }

    internal static void RegisterTrustedFeature(Type type, string featureId)
    {
        if (!ProductAttributeNames.IsAllowedValue(ProductAttributeNames.FeatureId, featureId))
        {
            return;
        }

        s_trustedFeatures.Remove(type);
        s_trustedFeatures.Add(type, new TrustedFeature(featureId));
    }

    internal static void RegisterBuiltInFeature(Type type, string featureId)
    {
        if (!featureId.StartsWith("builtin/", StringComparison.Ordinal)
            || !ProductAttributeNames.IsAllowedValue(ProductAttributeNames.FeatureId, featureId))
        {
            return;
        }

        lock (s_builtinFeatureGate)
        {
            s_builtinFeatures[type] = new TrustedFeature(featureId);
        }
    }

    internal static void UnregisterTrustedFeature(Type type)
    {
        s_trustedFeatures.Remove(type);
    }

    internal static string GetTrustedFeatureId(Type type)
    {
        lock (s_builtinFeatureGate)
        {
            if (s_builtinFeatures.TryGetValue(type, out TrustedFeature? builtInFeature))
            {
                return builtInFeature.Id;
            }
        }

        return s_trustedFeatures.TryGetValue(type, out TrustedFeature? feature)
            ? feature.Id
            : "generic";
    }

    private static Action<bool> StartExtensionLoadAnalytics()
    {
        ProductOperation operation = StartProductOperation(ProductEventNames.ExtensionLoad);
        return succeeded =>
        {
            if (succeeded)
            {
                operation.Complete();
            }
            else
            {
                operation.Complete(ProductOutcomes.Failed, "extension-load-failed");
            }
        };
    }

    private static void RecordExtensionLoaded(Type type)
    {
        RecordProductEvent(
            ProductEventNames.ExtensionLoad,
            ProductOutcomes.Success,
            [new(ProductAttributeNames.FeatureId, GetTrustedFeatureId(type))]);
    }

    internal static void CompressLogFiles()
    {
        _ = Task.Run(() =>
        {
            try
            {
                string logDir = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "log");
                if (!Directory.Exists(logDir)) return;

                string[] files = Directory.GetFiles(logDir, "*.txt")
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                foreach (string file in files.Take(Math.Max(0, files.Length - 10)))
                {
                    File.Delete(file);
                }

                foreach (string file in files.Skip(Math.Max(0, files.Length - 10)).Take(Math.Max(0, files.Length - 5)))
                {
                    Compress(file);
                }
            }
            catch
            {
                // Local log cleanup must never affect application startup.
            }
        });
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _configuration.ConfigurationChanged -= OnConfigurationChanged;
            _productSummaries.Dispose();
            RecordProductEventCore(ProductEventNames.AppSessionEnd, ProductOutcomes.Success, null, null);
            FlushProductSummariesCore(allowDisposed: true);
            DisposeProviders();
            _diagnosticLoggerFactory?.Dispose();
            _diagnosticLoggerFactory = null;
            _localLoggerFactory.Dispose();
        }

        lock (s_instanceGate)
        {
            if (ReferenceEquals(Instance, this)) Instance = null;
        }
    }

    internal void ReportDiagnostic(string component, string code, string outcome)
    {
        Microsoft.Extensions.Logging.ILogger? logger;
        lock (_gate)
        {
            logger = _diagnosticLoggerFactory?.CreateLogger(TelemetryDiagnostics.CategoryName);
        }

        logger?.LogInformation("safe diagnostic {Component} {Code} {Outcome}", component, code, outcome);
    }

    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_configuration.UsageAnalytics != true)
            {
                // Revoke both recorders and exporter threads before touching disk or
                // rebuilding providers. Reset can briefly wait for another process.
                Volatile.Write(ref _usageAnalyticsEnabled, 0);
                // Revocation means subsequent telemetry uses a fresh no-identity provider and
                // there is no durable client-side queue to flush.
                _identityStore.Reset();
                _productSummaries.Clear();
            }

            RebuildProviders();
        }
    }

    private void RebuildProviders()
    {
        bool usageAnalyticsEnabled = _configuration.UsageAnalytics == true;
        // This is read by batch exporter threads. Always revoke the outgoing
        // provider before disposing it. A replacement provider must never flush
        // a previous identity's queued batch after consent or identity changes.
        Volatile.Write(ref _usageAnalyticsEnabled, 0);
        _activeInstallationId = null;
        DisposeProviders();
        _diagnosticLoggerFactory?.Dispose();
        _diagnosticLoggerFactory = null;

        TelemetryIdentity? productIdentity = null;
        if (usageAnalyticsEnabled)
        {
            productIdentity = _identityStore.GetOrCreate();
            var traceExporterOptions = new OtlpExporterOptions();
            ConfigureExporter(traceExporterOptions, "traces");
            _productTracerProvider = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(CreateProductResource(productIdentity))
                .AddSource(ProductAnalytics.ActivitySourceName)
                .AddProcessor(new BatchActivityExportProcessor(
                    new ProductOtlpTraceExporter(traceExporterOptions, IsUsageAnalyticsEnabled)))
                .Build();

            var metricExporterOptions = new OtlpExporterOptions();
            ConfigureExporter(metricExporterOptions, "metrics");
            var qualityReader = new PeriodicExportingMetricReader(
                new QualityOtlpMetricExporter(metricExporterOptions, IsUsageAnalyticsEnabled))
            {
                TemporalityPreference = MetricReaderTemporalityPreference.Delta
            };
            _meterProvider = Sdk.CreateMeterProviderBuilder()
                .SetResourceBuilder(CreateQualityResource())
                .AddMeter(ProductAnalytics.MeterName)
                .AddView(
                    QualityMetricNames.OperationDuration,
                    QualityExportContract.CreateDurationHistogramConfiguration())
                .AddReader(qualityReader)
                .Build();

            if (_hostKind == TelemetryHostKind.Desktop
                && Interlocked.CompareExchange(ref _uncleanSessionRecorded, 1, 0) == 0
                && TelemetryUncleanSessionMarker.Exists())
            {
                ProductAnalytics.RecordUncleanSession();
            }
        }

        string[] sources = GetOperationalSources();
        if (sources.Length > 0)
        {
            _operationalTracerProvider = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(CreateOperationalResource())
                .AddSource(sources)
                .AddProcessor(new OperationalAttributeAllowlistProcessor())
                .AddOtlpExporter(options => ConfigureExporter(options, "traces"))
                .Build();
        }

        if (_configuration.Beutl_Logging == true)
        {
            _diagnosticLoggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddOpenTelemetry(options =>
                {
                    options.SetResourceBuilder(CreateOperationalResource());
                    options.IncludeFormattedMessage = false;
                    options.ParseStateValues = true;
                    options.AddOtlpExporter((exporterOptions, _) => ConfigureExporter(exporterOptions, "logs"));
                });
            });
        }

        // Do this only after the new product provider is ready. The identity gate
        // emits exactly one start event per installation in this process, while a
        // disable/reset makes the next enabled provider use a fresh installation.
        Volatile.Write(ref _usageAnalyticsEnabled, usageAnalyticsEnabled ? 1 : 0);
        if (productIdentity is not null)
        {
            _activeInstallationId = productIdentity.InstallationId;
            RecordSessionStart(productIdentity);
        }
    }

    private string[] GetOperationalSources()
    {
        var sources = new List<string>(3);
        if (_configuration.Beutl_Application == true) sources.Add("Beutl.Application");
        if (_configuration.Beutl_PackageManagement == true) sources.Add("Beutl.PackageManagement");
        if (_configuration.Beutl_Api_Client == true) sources.Add("Beutl.Api.Client");
        return [.. sources];
    }

    private ProductOperation StartProductOperationCore(
        string name,
        IEnumerable<KeyValuePair<string, string>>? attributes)
    {
        if (!IsUsageAnalyticsEnabled() || !ProductEventNames.All.Contains(name))
        {
            ProductAnalytics.ProductEventRejected.Add(1);
            return new ProductOperation(null, null);
        }

        // Product spans are deliberately trace roots. They must not inherit an
        // operational trace context, and they restore the ambient activity before
        // application code continues so the two export streams cannot parent each other.
        Activity? previous = Activity.Current;
        Activity? activity;
        try
        {
            // ActivitySource falls back to Activity.Current when an invalid/default
            // ActivityContext is supplied. Clear it briefly so this is a true root.
            Activity.Current = null;
            activity = ProductAnalytics.ActivitySource.StartActivity(name, ActivityKind.Internal);
        }
        finally
        {
            Activity.Current = previous;
        }
        if (activity is null)
        {
            return new ProductOperation(null, null);
        }

        AddProductCommonTags(activity);
        AddAllowedAttributes(activity, attributes);
        return new ProductOperation(
            activity,
            (outcome, errorCode, duration) => CompleteProductActivity(activity, outcome, errorCode, duration),
            previous);
    }

    private void RecordProductEventCore(
        string name,
        string outcome,
        IEnumerable<KeyValuePair<string, string>>? attributes,
        string? errorCode)
    {
        using ProductOperation operation = StartProductOperationCore(name, attributes);
        operation.Complete(outcome, errorCode);
    }

    private ProductSummaryOperation StartProductSummaryCore(string name, string trigger, string? featureId)
    {
        if (!IsUsageAnalyticsEnabled()
            || !ProductEventNames.All.Contains(name)
            || !ProductAttributeNames.IsAllowedValue(ProductAttributeNames.Trigger, trigger)
            || (featureId is not null
                && !ProductAttributeNames.IsAllowedValue(ProductAttributeNames.FeatureId, featureId)))
        {
            ProductAnalytics.ProductEventRejected.Add(1);
            return new ProductSummaryOperation(null);
        }

        var key = new ProductSummaryKey(name, trigger, featureId);
        return new ProductSummaryOperation((outcome, errorCode, duration) =>
            CompleteProductSummaryCore(key, outcome, errorCode, duration));
    }

    private void CompleteProductSummaryCore(
        ProductSummaryKey key,
        string outcome,
        string? errorCode,
        double durationMilliseconds)
    {
        if (!IsUsageAnalyticsEnabled() || !ProductOutcomes.All.Contains(outcome))
        {
            ProductAnalytics.ProductEventRejected.Add(1);
            return;
        }

        string? safeErrorCode = errorCode is not null
            && ProductAttributeNames.IsAllowedValue(ProductAttributeNames.ErrorCode, errorCode)
            ? errorCode
            : null;
        double safeDuration = NormalizeDuration(durationMilliseconds);
        _productSummaries.Add(key, outcome, safeErrorCode, safeDuration);
        if (key.Name == ProductEventNames.PreviewPlaybackSummary)
        {
            ProductAnalytics.RecordQualityOperation(key.Name, outcome, safeDuration);
        }
    }

    private void FlushProductSummaries()
    {
        lock (_gate)
        {
            if (_disposed) return;
            FlushProductSummariesCore(allowDisposed: false);
        }
    }

    private void FlushProductSummariesCore(bool allowDisposed)
    {
        if ((!allowDisposed && _disposed) || !IsUsageAnalyticsEnabled())
        {
            _productSummaries.Clear();
            return;
        }

        foreach (ProductSummarySnapshot snapshot in _productSummaries.Drain())
        {
            var attributes = new List<KeyValuePair<string, string>>(3)
            {
                new(ProductAttributeNames.Trigger, snapshot.Key.Trigger),
                new(ProductAttributeNames.CountBucket, GetCountBucket(snapshot.Count))
            };
            if (snapshot.Key.FeatureId is not null)
            {
                attributes.Add(new(ProductAttributeNames.FeatureId, snapshot.Key.FeatureId));
            }

            using ProductOperation operation = StartProductOperationCore(snapshot.Key.Name, attributes);
            operation.Complete(snapshot.Outcome, snapshot.ErrorCode, snapshot.AverageDurationMilliseconds);
        }
    }

    private void ResetUsageIdentityCore()
    {
        lock (_gate)
        {
            // An identity reset is a privacy boundary. Do not allow the old
            // provider to drain data while the replacement is being constructed.
            Volatile.Write(ref _usageAnalyticsEnabled, 0);
            _identityStore.Reset();
            _productSummaries.Clear();
            if (_configuration.UsageAnalytics == true)
            {
                RebuildProviders();
            }
        }
    }

    private void RecordSessionStart(TelemetryIdentity identity)
    {
        if (string.Equals(
                _sessionStartedInstallationId,
                identity.InstallationId,
                StringComparison.Ordinal))
        {
            return;
        }

        _sessionStartedInstallationId = identity.InstallationId;
        RecordProductEventCore(ProductEventNames.AppSessionStart, ProductOutcomes.Success, null, null);
    }

    private void AddProductCommonTags(Activity activity)
    {
        activity.SetTag("beutl.event.id", Guid.NewGuid().ToString("N"));
    }

    private static void AddAllowedAttributes(Activity activity, IEnumerable<KeyValuePair<string, string>>? attributes)
    {
        if (attributes is null) return;
        foreach (KeyValuePair<string, string> attribute in attributes)
        {
            if (ProductAttributeNames.All.Contains(attribute.Key)
                && ProductAttributeNames.IsAllowedValue(attribute.Key, attribute.Value))
            {
                activity.SetTag(attribute.Key, attribute.Value);
            }
        }
    }

    private static void CompleteProductActivity(Activity activity, string outcome, string? errorCode, double durationMilliseconds)
    {
        string effectiveOutcome = ProductOutcomes.All.Contains(outcome) ? outcome : ProductOutcomes.Failed;
        activity.SetTag("beutl.outcome", effectiveOutcome);
        double safeDuration = NormalizeDuration(durationMilliseconds);
        activity.SetTag(ProductAttributeNames.DurationMilliseconds, safeDuration);
        if (!string.IsNullOrEmpty(errorCode)
            && ProductAttributeNames.IsAllowedValue(ProductAttributeNames.ErrorCode, errorCode))
        {
            activity.SetTag(ProductAttributeNames.ErrorCode, errorCode);
        }
        if (effectiveOutcome == ProductOutcomes.Failed)
        {
            activity.SetStatus(ActivityStatusCode.Error);
        }
        if (activity.OperationName != ProductEventNames.PreviewPlaybackSummary)
        {
            ProductAnalytics.RecordQualityOperation(activity.OperationName, effectiveOutcome, safeDuration);
        }
        ProductAnalytics.ProductEventRecorded.Add(1);
    }

    private static double NormalizeDuration(double durationMilliseconds)
    {
        return double.IsFinite(durationMilliseconds) && durationMilliseconds >= 0
            ? Math.Min(Math.Round(durationMilliseconds, 3), 86_400_000)
            : 0;
    }

    private static string GetCountBucket(int count)
    {
        return count switch
        {
            <= 1 => "1",
            <= 5 => "2-5",
            <= 10 => "6-10",
            <= 50 => "11-50",
            _ => "51+"
        };
    }

    private bool IsUsageAnalyticsEnabled() => Volatile.Read(ref _usageAnalyticsEnabled) != 0;

    private ResourceBuilder CreateOperationalResource()
    {
        return ResourceBuilder.CreateEmpty()
            .AddAttributes(
            [
                new KeyValuePair<string, object>("service.name", _serviceName),
                new KeyValuePair<string, object>("service.version", BeutlApplication.Version),
                new KeyValuePair<string, object>("beutl.release.channel", "stable"),
                new KeyValuePair<string, object>("os.type", GetOperatingSystem()),
                new KeyValuePair<string, object>("process.architecture", RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()),
                new KeyValuePair<string, object>("beutl.renderer", "unknown")
            ]);
    }

    // Metrics must remain aggregate-only: they carry the product stream marker for
    // Collector routing but deliberately omit installation/session identifiers.
    private ResourceBuilder CreateQualityResource()
    {
        return CreateOperationalResource().AddAttributes(
        [
            new KeyValuePair<string, object>("beutl.telemetry.stream", "product"),
            new KeyValuePair<string, object>("beutl.analytics.schema", ProductSchemaVersion)
        ]);
    }

    private ResourceBuilder CreateProductResource(TelemetryIdentity identity)
    {
        return CreateOperationalResource().AddAttributes(
        [
            new KeyValuePair<string, object>("beutl.telemetry.stream", "product"),
            new KeyValuePair<string, object>("beutl.analytics.schema", ProductSchemaVersion),
            new KeyValuePair<string, object>("beutl.installation.id", identity.InstallationId),
            new KeyValuePair<string, object>("beutl.session.id", _sessionId),
            new KeyValuePair<string, object>("beutl.first_seen_month", identity.FirstSeenMonth)
        ]);
    }

    private static void ConfigureExporter(OtlpExporterOptions options, string signal)
    {
        string configured = Environment.GetEnvironmentVariable("BEUTL_OTLP_ENDPOINT") ?? "https://otel.beditor.net/";
        Uri baseUri = new(configured.EndsWith('/') ? configured : $"{configured}/", UriKind.Absolute);
        options.Endpoint = new Uri(baseUri, $"v1/{signal}");
        options.Protocol = OtlpExportProtocol.HttpProtobuf;
    }

    private void DisposeProviders()
    {
        _operationalTracerProvider?.Dispose();
        _operationalTracerProvider = null;
        _productTracerProvider?.Dispose();
        _productTracerProvider = null;
        _meterProvider?.Dispose();
        _meterProvider = null;
    }

    private static string GetOperatingSystem()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsLinux()) return "linux";
        if (OperatingSystem.IsMacOS()) return "macos";
        return "other";
    }

    internal static bool IsSessionId(string? value)
        => value is not null && Guid.TryParseExact(value, "N", out _);

    internal static string GetServiceName(TelemetryHostKind hostKind)
    {
        return hostKind == TelemetryHostKind.PackageTools
            ? "beutl.package-tools"
            : "beutl.desktop";
    }

    private static ILoggerFactory SetupLocalLogger(TelemetryHostKind hostKind)
    {
        string timestamp = DateTimeOffset.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        string logDir = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "log");
        Directory.CreateDirectory(logDir);
        string logFile = Path.Combine(logDir, $"log{timestamp}-{Environment.ProcessId}.txt");
        const string outputTemplate =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext:l}] {Message:lj}{NewLine}{Exception}";

        LoggerConfiguration loggerConfiguration = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Verbose();

#if DEBUG
        if (hostKind == TelemetryHostKind.Desktop)
        {
            loggerConfiguration = loggerConfiguration.WriteTo.Debug(
                outputTemplate: outputTemplate,
                restrictedToMinimumLevel: LogEventLevel.Verbose);
        }
#endif

        Serilog.Log.Logger = loggerConfiguration
            .WriteTo.Async(writeTo => writeTo.File(logFile, outputTemplate: outputTemplate,
                restrictedToMinimumLevel: LogEventLevel.Information))
            .CreateLogger();

        ILoggerFactory loggerFactory = LoggerFactory.Create(
            builder => builder.AddSerilog(Serilog.Log.Logger, dispose: true));
        Beutl.Logging.Log.LoggerFactory = loggerFactory;
        return loggerFactory;
    }

    private static void Compress(string file)
    {
        string target = Path.ChangeExtension(file, ".gz");
        using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var destination = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
        using var gzip = new GZipStream(destination, CompressionLevel.SmallestSize);
        source.CopyTo(gzip);
        File.Delete(file);
    }

    private sealed class OperationalAttributeAllowlistProcessor : BaseProcessor<Activity>
    {
        private static readonly FrozenSet<string> s_allowed =
        ["appVersion", "minAppVersion", "itemsCount", "width", "height", "framerate", "samplerate"];

        public override void OnEnd(Activity data)
        {
            foreach (KeyValuePair<string, object?> tag in data.TagObjects.ToArray())
            {
                if (!s_allowed.Contains(tag.Key))
                {
                    data.SetTag(tag.Key, null);
                }
            }

            if (data.Status != ActivityStatusCode.Unset)
            {
                data.SetStatus(data.Status);
            }
        }
    }

    private sealed record TrustedFeature(string Id);
}
