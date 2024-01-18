#pragma warning disable CS0436

using System.IO.Compression;
using System.Runtime.CompilerServices;

using Avalonia.Threading;

using Azure.Monitor.OpenTelemetry.Exporter;

using Beutl.Configuration;

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;

using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using Serilog;

namespace Beutl.Services;

internal static partial class Telemetry
{
    private static readonly TelemetryClient s_client;
    private static readonly TelemetryConfiguration s_config;
    private static TracerProvider? s_tracerProvider;
    private const string Instrumentation = "b8cc7df1-1367-41f5-a819-5c95a10075cb";

    static Telemetry()
    {
        s_config = TelemetryConfiguration.CreateDefault();
        s_config.ConnectionString = $"InstrumentationKey={Instrumentation}";
        s_client = new TelemetryClient(s_config);
        s_client.Context.Session.Id = Guid.NewGuid().ToString();
        s_client.Context.Location.Ip = "N/A";
        s_client.Context.Cloud.RoleInstance = "N/A";
        s_client.Context.GlobalProperties.Add("Beutl Version", GitVersionInformation.NuGetVersion);

        s_tracerProvider = CreateTracer();

        SetupLogger();
    }

    public static ActivitySource Applilcation { get; } = new("Beutl.Application", GitVersionInformation.SemVer);

    private static TracerProvider? CreateTracer()
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
                .SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService("Beutl", serviceVersion: GitVersionInformation.NuGetVersionV2))
                .AddSource([.. list])
                //.AddZipkinExporter()
                .AddAzureMonitorTraceExporter(b => b.ConnectionString = $"InstrumentationKey={Instrumentation}")
                .Build();
        }
    }

    public static IDisposable GetDisposable()
    {
        return new Disposable();
    }

    public static void RecreateTracer()
    {
        s_tracerProvider?.Dispose();
        s_tracerProvider = CreateTracer();
    }

    public static Activity? StartActivity([CallerMemberName] string name = "", ActivityKind kind = ActivityKind.Internal)
    {
        return Applilcation.StartActivity(name, kind);
    }

    private sealed class Disposable : IDisposable
    {
        public void Dispose()
        {
            s_tracerProvider?.Dispose();
            s_client.Flush();
        }
    }

    private static void SetupLogger()
    {
        string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        int pid = Environment.ProcessId;

        string logDir = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "log");
        string logFile = Path.Combine(logDir, $"log{timestamp}-{pid}.txt");
        const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext:l}] {Message:lj}{NewLine}{Exception}";
        LoggerConfiguration config = new LoggerConfiguration()
            .Enrich.FromLogContext();

#if DEBUG
        config = config.MinimumLevel.Verbose()
            .WriteTo.Debug(outputTemplate: OutputTemplate);
#else
        config = config.MinimumLevel.Debug();
#endif
        config = config.WriteTo.Async(b => b.File(logFile, outputTemplate: OutputTemplate));
        if (GlobalConfiguration.Instance.TelemetryConfig.Beutl_Logging == true)
        {
            config = config.WriteTo.ApplicationInsights(s_client, TelemetryConverter.Traces);
        }

        Log.Logger = config.CreateLogger();

        BeutlApplication.Current.LoggerFactory = LoggerFactory.Create(builder => builder.AddSerilog(Log.Logger, true));
    }

    public static void CompressLogFiles()
    {
        Dispatcher.UIThread.Invoke(async () =>
        {
            Serilog.ILogger log = Log.ForContext(typeof(Telemetry));
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
                log.Error(ex, "Exception occurred while compressing logs");
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
}
