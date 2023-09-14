#pragma warning disable CS0436

using System.Runtime.CompilerServices;

using Azure.Monitor.OpenTelemetry.Exporter;

using Beutl.Configuration;

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;

using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Beutl.Services;

internal static partial class Telemetry
{
    private static readonly TelemetryClient s_client;
    private static TracerProvider? s_tracerProvider;
    private static TelemetryConfiguration s_config;
    private const string Instrumentation = "b8cc7df1-1367-41f5-a819-5c95a10075cb";

    static Telemetry()
    {
        s_config = TelemetryConfiguration.CreateDefault();
        s_config.ConnectionString = $"InstrumentationKey={Instrumentation}";
        s_client = new TelemetryClient(s_config);
        s_client.Context.Session.Id = Guid.NewGuid().ToString();
        s_client.Context.Location.Ip = "N/A";
        s_client.Context.Cloud.RoleInstance = "N/A";

        s_tracerProvider = CreateTracer();
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
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Beutl"))
                .AddSource(list.ToArray())
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
}
