#pragma warning disable CS0436

using System.Collections;
using System.IO.Compression;
using System.Runtime.CompilerServices;

using Avalonia.Threading;

using Azure.Monitor.OpenTelemetry.Exporter;

using Beutl.Configuration;

using Microsoft.Extensions.Logging;

using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using Serilog;

namespace Beutl.Services;

internal class Telemetry : IDisposable
{
    private static readonly KeyValuePair<string, object?> s_versionAttribute = new("service.version", GitVersionInformation.NuGetVersion);
    private readonly TracerProvider? _tracerProvider;
    private readonly Lazy<ResourceBuilder> _resourceBuilder;
    internal readonly string _sessionId;
    private const string Instrumentation = "b8cc7df1-1367-41f5-a819-5c95a10075cb";

    public Telemetry(string? sessionId = null)
    {
        _sessionId = sessionId ?? Guid.NewGuid().ToString();
        _resourceBuilder = new(() =>
        {
            return ResourceBuilder.CreateEmpty()
                .AddService("Beutl", serviceVersion: GitVersionInformation.NuGetVersion, serviceInstanceId: _sessionId);
        });
        _tracerProvider = CreateTracer();

        SetupLogger();
    }

    public static ActivitySource Applilcation { get; } = new("Beutl.Application", GitVersionInformation.SemVer);

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
                .AddSource([.. list])
                .AddAzureMonitorTraceExporter(b => b.ConnectionString = $"InstrumentationKey={Instrumentation}")
                .Build();
        }
    }

    public static IDisposable GetDisposable(string? sessionId = null)
    {
        Instance = new Telemetry(sessionId);
        return Instance;
    }

    public static Activity? StartActivity([CallerMemberName] string name = "", ActivityKind kind = ActivityKind.Internal)
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
        const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext:l}] {Message:lj}{NewLine}{Exception}";
        LoggerConfiguration config = new LoggerConfiguration()
            .Enrich.FromLogContext();

#if DEBUG && !Beutl_PackageTools
        config = config.MinimumLevel.Verbose()
            .WriteTo.Debug(outputTemplate: OutputTemplate);
#else
        config = config.MinimumLevel.Debug();
#endif
        config = config.WriteTo.Async(b => b.File(logFile, outputTemplate: OutputTemplate));

        Log.Logger = config.CreateLogger();

        Logging.Log.LoggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSerilog(Log.Logger, true);

            if (GlobalConfiguration.Instance.TelemetryConfig.Beutl_Logging == true)
            {
                builder.AddOpenTelemetry(o => o
                    .SetResourceBuilder(_resourceBuilder.Value)
                    .AddProcessor(new AddVersionLogProcessor())
                    .AddAzureMonitorLogExporter(az => az.ConnectionString = $"InstrumentationKey={Instrumentation}"));
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
            data.SetTag(s_versionAttribute.Key, s_versionAttribute.Value);
        }
    }

    internal class AddVersionLogProcessor : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data)
        {
            base.OnEnd(data);
            data.Attributes = new AppendVersionAttribute(data.Attributes);
        }

        private sealed class AppendVersionAttribute(IReadOnlyList<KeyValuePair<string, object?>>? source) : IReadOnlyList<KeyValuePair<string, object?>>
        {
            public KeyValuePair<string, object?> this[int index]
            {
                get
                {
                    ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

                    if (index < source?.Count)
                    {
                        return source[index];
                    }

                    return s_versionAttribute;
                }
            }

            public int Count => (source?.Count ?? 0) + 1;

            public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
            {
                if (source != null)
                {
                    return source.Append(s_versionAttribute)
                        .GetEnumerator();
                }
                else
                {
                    return Enumerable.Repeat(s_versionAttribute, 1).GetEnumerator();
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
