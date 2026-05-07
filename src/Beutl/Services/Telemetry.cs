#pragma warning disable CS0436

using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    private static readonly KeyValuePair<string, object>[] s_attributes;
    private static readonly string s_version;
    private readonly TracerProvider? _tracerProvider;
    private readonly Lazy<ResourceBuilder> _resourceBuilder;
    internal readonly string _sessionId;

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
    }

    public static ActivitySource Applilcation { get; } = new("Beutl.Application", s_version);

    public static Telemetry Instance { get; private set; } = null!;

    private TracerProvider? CreateTracer()
    {
        TelemetryConfig t = GlobalConfiguration.Instance.TelemetryConfig;
        var list = new List<string>(4);
        if (t.Beutl_Application == true)
            list.Add("Beutl.Application");

        if (t.Beutl_PackageManagement == true)
            list.Add("Beutl.PackageManagement");

        if (t.Beutl_Api_Client == true)
            list.Add("Beutl.Api.Client");

        if (list.Count == 0)
        {
            return null;
        }
        else
        {
            return Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(_resourceBuilder.Value)
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

    public void Dispose()
    {
        _tracerProvider?.Dispose();
        Logging.Log.LoggerFactory.Dispose();
    }

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

            if (GlobalConfiguration.Instance.TelemetryConfig.Beutl_Logging == true)
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
                            var files = Directory.GetFiles(logDir).ToList();
                            files.Sort((x, y) => string.Compare(x, y, StringComparison.OrdinalIgnoreCase));

                            if (files.Count > 10)
                            {
                                int deleteCount = files.Count - 10;
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

                            if (files.Count > 5)
                            {
                                int compressCount = files.Count - 5;
                                foreach (string? item in files.Take(compressCount))
                                {
                                    ReplaceCompressedFile(item);
                                }

                                files.RemoveRange(0, compressCount);
                            }
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
        private static readonly (string Value, string Token)[] s_patterns;

        static SensitiveData()
        {
            var list = new List<(string Value, string Token)>(4);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
                list.Add((home, "<Home>"));

            string temp = Path.GetTempPath();
            if (!string.IsNullOrWhiteSpace(temp))
                list.Add((temp, "<Temp>"));

            string user = Environment.UserName;
            if (!string.IsNullOrWhiteSpace(user))
                list.Add((user, "<User>"));

            string machine = Environment.MachineName;
            if (!string.IsNullOrWhiteSpace(machine))
                list.Add((machine, "<Machine>"));

            // Temp が Home の subset になる場合 (Windows: %LOCALAPPDATA%\Temp) に
            // Home が先に当たって <Home>\...\Temp\ のように残るのを防ぐため、長い順に置換する。
            list.Sort((a, b) => b.Value.Length.CompareTo(a.Value.Length));
            s_patterns = [.. list];
        }

        public static string? Sanitize(string? input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            string current = input;
            foreach ((string value, string token) in s_patterns)
            {
                if (current.IndexOf(value, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                current = current.Replace(value, token, StringComparison.OrdinalIgnoreCase);
            }
            return current;
        }
    }

    internal class RemoveSensitiveDataProcessor : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data)
        {
            base.OnEnd(data);

            foreach (KeyValuePair<string, string?> pair in data.Tags)
            {
                string? sanitized = SensitiveData.Sanitize(pair.Value);
                if (!ReferenceEquals(sanitized, pair.Value))
                {
                    data.SetTag(pair.Key, sanitized);
                }
            }

            string? sanitizedName = SensitiveData.Sanitize(data.DisplayName);
            if (sanitizedName is not null && !ReferenceEquals(sanitizedName, data.DisplayName))
            {
                data.DisplayName = sanitizedName;
            }

            if (!string.IsNullOrEmpty(data.StatusDescription))
            {
                string? sanitizedDesc = SensitiveData.Sanitize(data.StatusDescription);
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
                    : [..data.Attributes, ..extra];
                data.Exception = null;
            }
        }
    }
}
