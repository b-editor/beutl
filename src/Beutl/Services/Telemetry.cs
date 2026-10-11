#pragma warning disable CS0436

using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using Beutl.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace Beutl.Services;

internal class Telemetry : IDisposable
{
    private const int MaxLogFiles = 10;
    private const int MaxUncompressedLogFiles = 5;
    private static readonly KeyValuePair<string, object>[] s_attributes;
    private static readonly string s_version;
    private readonly TracerProvider? _tracerProvider;
    private readonly Lazy<ResourceBuilder> _resourceBuilder;
    internal readonly string _sessionId;
#if !Beutl_PackageTools
    private readonly UsageTelemetry _usageTelemetry;
#endif

#if true
    private static string BaseUrl = "https://otel.beditor.net";
#else
    private static string BaseUrl = "http://localhost:4318";
#endif

    static Telemetry()
    {
        static string GetSystemType()
        {
            if (OperatingSystem.IsWindows()) return "windows";
            else if (OperatingSystem.IsLinux()) return "linux";
            else if (OperatingSystem.IsMacOS()) return "macos";
            else return Environment.OSVersion.Platform.ToString();
        }

        var asm = Assembly.GetEntryAssembly()!;
        var att = asm.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(i => i.Key == "NuGetVersion");
        s_version = att?.Value ?? "Unknown";
        s_attributes =
        [
            new("service.version", s_version),
            new("os.type", GetSystemType()),
            new("os.description", Environment.OSVersion.VersionString),
            new("os.name", OperatingSystem.IsLinux() ? LinuxDistro.Id
                : OperatingSystem.IsWindows() ? "Windows"
                : OperatingSystem.IsMacOS() ? "Mac OS X"
                : "Unknown"),
        ];
    }

    public Telemetry(string? sessionId = null)
    {
        _sessionId = sessionId ?? Guid.NewGuid().ToString();
        _resourceBuilder = new(() =>
        {
            return ResourceBuilder.CreateDefault()
                .AddService("Beutl", serviceVersion: s_version, serviceInstanceId: _sessionId)
                .AddAttributes(s_attributes);
        });
        _tracerProvider = CreateTracer();

        SetupLogger();
#if !Beutl_PackageTools
        UsageTelemetry.Current = _usageTelemetry = new(GlobalConfiguration.Instance.TelemetryConfig);
#endif
    }

    public static ActivitySource Applilcation { get; } = new("Beutl.Application", s_version);

    public static Telemetry Instance { get; private set; } = null!;

    // The names of the ActivitySources the tracer listens to under the given consent.
    internal static List<string> GetActivitySourceNames(TelemetryConfig t)
    {
        var list = new List<string>(4);
        bool configured = IsConsentConfigured(t);
        if (configured && t.Beutl_Application == true)
            list.Add("Beutl.Application");

        if (configured && t.Beutl_PackageManagement == true)
            list.Add(Api.Services.PackageManagementActivitySource.Name);

        if (configured && t.Beutl_Api_Client == true)
            list.Add("Beutl.Api.Client");
#if !Beutl_PackageTools
        // Keep the usage listener installed so consent can change without a restart.
        // UsageTelemetry itself never records without current application consent.
        list.Add(UsageTelemetry.SourceName);
#endif
        return list;
    }

    private TracerProvider? CreateTracer()
    {
        List<string> list = GetActivitySourceNames(GlobalConfiguration.Instance.TelemetryConfig);
        if (list.Count == 0)
        {
            return null;
        }
        else
        {
            return Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(_resourceBuilder.Value)
#if !Beutl_PackageTools
                .SetSampler(new UsageSampler())
#endif
                .AddProcessor(new AddVersionActivityProcessor())
                .AddProcessor(new RemoveSensitiveDataProcessor())
                .AddSource([.. list])
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri($"{BaseUrl}/v1/traces");
                    options.Protocol = OtlpExportProtocol.HttpProtobuf;
                })
                .Build();
        }
    }

    public static IDisposable GetDisposable(string? sessionId = null)
    {
        Instance = new Telemetry(sessionId);
        return Instance;
    }

    public static Activity? StartActivity([CallerMemberName] string name = "",
        ActivityKind kind = ActivityKind.Internal)
    {
        return Applilcation.StartActivity(name, kind);
    }

    internal static bool IsConsentConfigured(TelemetryConfig config)
    {
        return config.Beutl_Api_Client.HasValue
            && config.Beutl_Application.HasValue
            && config.Beutl_PackageManagement.HasValue
            && config.Beutl_Logging.HasValue;
    }

    public void Dispose()
    {
#if !Beutl_PackageTools
        _usageTelemetry.Dispose();
#endif
        _tracerProvider?.Dispose();
        Logging.Log.LoggerFactory.Dispose();
    }

#if !Beutl_PackageTools
    private sealed class UsageSampler : Sampler
    {
        private readonly ParentBasedSampler _default = new(new AlwaysOnSampler());

        public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
        {
            if (samplingParameters.Name == "Usage.Summary")
                return new SamplingResult(UsageTelemetry.HasConsent(GlobalConfiguration.Instance.TelemetryConfig)
                    ? SamplingDecision.RecordAndSample : SamplingDecision.Drop);
            return _default.ShouldSample(samplingParameters);
        }
    }
#endif

    private void SetupLogger()
    {
        string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        int pid = Environment.ProcessId;

        string logDir = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "log");
        string logFile = Path.Combine(logDir, $"log{timestamp}-{pid}.txt");
        const string OutputTemplate =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext:l}] {Message:lj}{NewLine}{Exception}";
        LoggerConfiguration config = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Verbose();

#if DEBUG && !Beutl_PackageTools
        config = config
            .WriteTo.Debug(outputTemplate: OutputTemplate, restrictedToMinimumLevel: LogEventLevel.Verbose);
#endif
        config = config
            .WriteTo.Async(b => b.File(logFile, outputTemplate: OutputTemplate, restrictedToMinimumLevel: LogEventLevel.Information));

        Log.Logger = config.CreateLogger();

        Logging.Log.LoggerFactory = LoggerFactory.Create(builder =>
        {
            // Serilogへ行くログはデフォルトでTrace以上
            builder.AddSerilog(Log.Logger, true);

            TelemetryConfig telemetryConfig = GlobalConfiguration.Instance.TelemetryConfig;
            if (IsConsentConfigured(telemetryConfig) && telemetryConfig.Beutl_Logging == true)
            {
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddOpenTelemetry(o =>
                {
                    o.SetResourceBuilder(_resourceBuilder.Value);
                    o.AddProcessor(new AddVersionLogProcessor());
                    o.AddProcessor(new RemoveSensitiveDataLogProcessor());
                    o.AddOtlpExporter((exporterOptions, _) =>
                    {
                        exporterOptions.Endpoint = new Uri($"{BaseUrl}/v1/logs");
                        exporterOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
                    });
                });
            }
        });
    }

    public static void CompressLogFiles()
    {
        Dispatcher.UIThread.Invoke(async () =>
        {
            Microsoft.Extensions.Logging.ILogger log
                = Logging.Log.LoggerFactory.CreateLogger(typeof(Telemetry));
            var mutex = new Mutex(false, "Beutl.Logging.Compression", out bool createdNew);
            try
            {
                if (createdNew)
                {
                    await Task.Run(() =>
                    {
                        string logDir = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "log");
                        if (Directory.Exists(logDir))
                        {
                            PruneLogDirectory(logDir);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Exception occurred while compressing logs");
            }
            finally
            {
                mutex.Close();
            }
        });
    }

    private static void PruneLogDirectory(string logDir)
    {
        var files = Directory.GetFiles(logDir).ToList();
        files.Sort((x, y) => string.Compare(x, y, StringComparison.OrdinalIgnoreCase));

        if (files.Count > MaxLogFiles)
        {
            int deleteCount = files.Count - MaxLogFiles;
            foreach (string? item in files.Take(deleteCount))
            {
                try
                {
                    File.Delete(item);
                }
                catch
                {
                }
            }

            files.RemoveRange(0, deleteCount);
        }

        if (files.Count > MaxUncompressedLogFiles)
        {
            int compressCount = files.Count - MaxUncompressedLogFiles;
            foreach (string? item in files.Take(compressCount))
            {
                ReplaceCompressedFile(item);
            }

            files.RemoveRange(0, compressCount);
        }
    }

    private static void ReplaceCompressedFile(string file)
    {
        try
        {
            using (var srcStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
            using (var dstStream = new FileStream(Path.ChangeExtension(file, "gz"), FileMode.Create))
            using (var dstGZipStream = new GZipStream(dstStream, CompressionLevel.SmallestSize))
            {
                srcStream.CopyTo(dstGZipStream);
            }

            File.Delete(file);
        }
        catch
        {
        }
    }

    internal class AddVersionActivityProcessor : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data)
        {
            base.OnEnd(data);
            foreach (KeyValuePair<string, object> item in s_attributes)
            {
                data.SetTag(item.Key, item.Value);
            }
        }
    }

    internal class AddVersionLogProcessor : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data)
        {
            base.OnEnd(data);
            if (data.Attributes != null)
            {
                data.Attributes = data.Attributes!.Concat(s_attributes).ToArray()!;
            }
            else
            {
                data.Attributes = s_attributes!;
            }
        }
    }

    internal static class SensitiveData
    {
        private readonly record struct Pattern(string Value, string Token, Regex? BoundaryRegex)
        {
            public bool UseBoundary => BoundaryRegex is not null;
        }

        private static readonly Pattern[] s_patterns;

        static SensitiveData()
        {
            var list = new List<Pattern>(4);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
                list.Add(new Pattern(home, "<Home>", null));

            string temp = Path.GetTempPath();
            if (!string.IsNullOrWhiteSpace(temp))
                list.Add(new Pattern(temp, "<Temp>", null));

            // UserName / MachineName は短く一般的な単語 (dev, pc, admin など) になり得るため、
            // 部分文字列の誤マッチを避けて識別子境界 (英数字以外) で挟まれた箇所のみ置換する。
            string user = Environment.UserName;
            if (!string.IsNullOrWhiteSpace(user))
                list.Add(new Pattern(user, "<User>", CreateBoundaryRegex(user)));

            string machine = Environment.MachineName;
            if (!string.IsNullOrWhiteSpace(machine))
                list.Add(new Pattern(machine, "<Machine>", CreateBoundaryRegex(machine)));

            // Temp が Home の subset になる場合 (Windows: %LOCALAPPDATA%\Temp) に
            // Home が先に当たって <Home>\...\Temp\ のように残るのを防ぐため、長い順に置換する。
            list.Sort((a, b) => b.Value.Length.CompareTo(a.Value.Length));
            s_patterns = [.. list];
        }

        private static Regex CreateBoundaryRegex(string value)
        {
            // (?<![A-Za-z0-9_]) <value> (?![A-Za-z0-9_]) — 識別子境界マッチ。
            // パスセグメント (\, /, ., 空白等) や行頭・行末でのみマッチさせる。
            return new Regex(
                $@"(?<![A-Za-z0-9_]){Regex.Escape(value)}(?![A-Za-z0-9_])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }

        public static string? Sanitize(string? input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            string current = input;
            foreach (Pattern pattern in s_patterns)
            {
                if (pattern.UseBoundary)
                {
                    current = pattern.BoundaryRegex!.Replace(current, pattern.Token);
                }
                else
                {
                    if (current.IndexOf(pattern.Value, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    current = current.Replace(pattern.Value, pattern.Token, StringComparison.OrdinalIgnoreCase);
                }
            }
            return current;
        }
    }

    internal class RemoveSensitiveDataProcessor(Func<string?, string?>? sanitize = null) : BaseProcessor<Activity>
    {
        private readonly Func<string?, string?> _sanitize = sanitize ?? SensitiveData.Sanitize;

        public override void OnEnd(Activity data)
        {
            base.OnEnd(data);

            bool usageSummary = false;
#if !Beutl_PackageTools
            // Only this internal source emits the controlled usage identifiers.
            // A diagnostic span with the same name must still be redacted.
            usageSummary = ReferenceEquals(data.Source, UsageTelemetry.Source)
                && data.OperationName == "Usage.Summary"
                && Equals(data.GetTagItem("beutl.usage.schema_version"), 1);
#endif
            foreach (KeyValuePair<string, string?> pair in data.Tags)
            {
                if (usageSummary && pair.Key is "beutl.usage.event" or "beutl.usage.tool"
                    or "beutl.usage.feature" or "beutl.usage.outcome") continue;
                string? sanitized = _sanitize(pair.Value);
                if (!ReferenceEquals(sanitized, pair.Value))
                {
                    data.SetTag(pair.Key, sanitized);
                }
            }

            string? sanitizedName = usageSummary && data.DisplayName == "Usage.Summary"
                ? data.DisplayName : _sanitize(data.DisplayName);
            if (sanitizedName is not null && !ReferenceEquals(sanitizedName, data.DisplayName))
            {
                data.DisplayName = sanitizedName;
            }

            if (!string.IsNullOrEmpty(data.StatusDescription))
            {
                string? sanitizedDesc = _sanitize(data.StatusDescription);
                if (!ReferenceEquals(sanitizedDesc, data.StatusDescription))
                {
                    data.SetStatus(data.Status, sanitizedDesc);
                }
            }
        }
    }

    internal class RemoveSensitiveDataLogProcessor : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data)
        {
            base.OnEnd(data);

            if (data.Body is { } body)
            {
                string? sanitized = SensitiveData.Sanitize(body);
                if (!ReferenceEquals(sanitized, body))
                {
                    data.Body = sanitized;
                }
            }

            if (data.FormattedMessage is { } formatted)
            {
                string? sanitized = SensitiveData.Sanitize(formatted);
                if (!ReferenceEquals(sanitized, formatted))
                {
                    data.FormattedMessage = sanitized;
                }
            }

            if (data.Attributes is { } attrs)
            {
                bool changed = false;
                foreach (KeyValuePair<string, object?> kv in attrs)
                {
                    if (kv.Value is string s &&
                        !ReferenceEquals(SensitiveData.Sanitize(s), s))
                    {
                        changed = true;
                        break;
                    }
                }

                if (changed)
                {
                    data.Attributes =
                    [
                        ..attrs.Select(kv => kv.Value is string s
                            ? new KeyValuePair<string, object?>(kv.Key, SensitiveData.Sanitize(s))
                            : kv)
                    ];
                }
            }

            if (data.Exception is { } ex)
            {
                var extra = new[]
                {
                    new KeyValuePair<string, object?>("exception.type",
                        ex.GetType().FullName ?? ex.GetType().Name),
                    new KeyValuePair<string, object?>("exception.message",
                        SensitiveData.Sanitize(ex.Message) ?? string.Empty),
                    new KeyValuePair<string, object?>("exception.stacktrace",
                        SensitiveData.Sanitize(ex.ToString()) ?? string.Empty),
                };
                data.Attributes = data.Attributes is null
                    ? extra
                    : [.. data.Attributes, .. extra];
                data.Exception = null;
            }
        }
    }
}
