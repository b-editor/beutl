#pragma warning disable CS0436

using System.Reflection;
using Avalonia.Controls;

using Beutl.Controls.Navigation;
using Beutl.Rendering;
using Beutl.Threading;

using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class InfomationPageViewModel : PageContext
{
    private TelemetrySettingsPageViewModel? _telemetry;

    public InfomationPageViewModel()
    {
        RenderThread.Dispatcher.Dispatch(() =>
        {
            if (!Design.IsDesignMode)
            {
                _ = SharedGRContext.GetOrCreate();
                GlVersion.Value = SharedGRContext.Version;

                GpuDevice.Value = SharedGPUContext.Device.Name;

                using var sw = new StringWriter();
                SharedGPUContext.Device.PrintInformation(sw);

                GpuDeviceDetail.Value = sw.ToString();
            }
        }, DispatchPriority.Low);

        NavigateToTelemetry = new AsyncReactiveCommand()
            .WithSubscribe(async () =>
            {
                INavigationProvider nav = await GetNavigation();
                await nav.NavigateAsync(
                    x => x is not null,
                    () => Telemetry);
            });

        BuildMetadata = typeof(InfomationPageViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "Unknown";
    }

    public string CurrentVersion { get; } = BeutlApplication.Version;

    public string BuildMetadata { get; }

    public string GitRepositoryUrl { get; } = "https://github.com/b-editor/beutl";

    public string LicenseUrl { get; } = "https://github.com/b-editor/beutl/blob/main/LICENSE";

    public string ThirdPartyNoticesUrl { get; } = "https://github.com/b-editor/beutl/blob/main/THIRD_PARTY_NOTICES.md";

    public ReactivePropertySlim<string?> GlVersion { get; } = new();

    public ReactivePropertySlim<string?> GpuDevice { get; } = new();

    public ReactivePropertySlim<string?> GpuDeviceDetail { get; } = new();

    public TelemetrySettingsPageViewModel Telemetry => _telemetry ??= new();

    public AsyncReactiveCommand NavigateToTelemetry { get; }
}
