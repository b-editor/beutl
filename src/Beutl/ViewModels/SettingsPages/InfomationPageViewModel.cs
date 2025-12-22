#pragma warning disable CS0436

using System.Reflection;
using Avalonia.Controls;

using Beutl.Controls.Navigation;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Threading;

using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class InformationPageViewModel : PageContext
{
    private TelemetrySettingsPageViewModel? _telemetry;

    public InformationPageViewModel()
    {
        RenderThread.Dispatcher.Dispatch(() =>
        {
            if (!Design.IsDesignMode)
            {
                var context = GraphicsContextFactory.GetOrCreateShared();
                if (context?.GpuInfo is { } gpuInfo)
                {
                    // Available GPUs
                    AvailableGpus.Value = gpuInfo.AvailableGpus
                        .Select(g => $"{g.Name} ({g.DeviceType})")
                        .ToArray();

                    // Selected GPU
                    if (gpuInfo.SelectedGpu != null)
                    {
                        SelectedGpu.Value = $"{gpuInfo.SelectedGpu.Name} ({gpuInfo.SelectedGpu.DeviceType})";
                    }

                    // Vulkan version
                    VulkanVersion.Value = gpuInfo.ApiVersion;

                    // Enabled extensions
                    EnabledExtensions.Value = gpuInfo.EnabledExtensions.ToArray();

                    // Memory info
                    if (gpuInfo.Memory != null)
                    {
                        var deviceMemoryMB = gpuInfo.Memory.DeviceLocalMemory / (1024 * 1024);
                        var hostMemoryMB = gpuInfo.Memory.HostVisibleMemory / (1024 * 1024);
                        AvailableMemory.Value = $"Device Local: {deviceMemoryMB:N0} MB, Host Visible: {hostMemoryMB:N0} MB";
                    }
                }
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

        BuildMetadata = typeof(InformationPageViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "Unknown";
    }

    public string CurrentVersion { get; } = BeutlApplication.Version;

    public string BuildMetadata { get; }

    public string GitRepositoryUrl { get; } = "https://github.com/b-editor/beutl";

    public string LicenseUrl { get; } = "https://github.com/b-editor/beutl/blob/main/LICENSE";

    public string ThirdPartyNoticesUrl { get; } = "https://github.com/b-editor/beutl/blob/main/THIRD_PARTY_NOTICES.md";

    public ReactivePropertySlim<string[]?> AvailableGpus { get; } = new();

    public ReactivePropertySlim<string?> SelectedGpu { get; } = new();

    public ReactivePropertySlim<string[]?> EnabledExtensions { get; } = new();

    public ReactivePropertySlim<string?> VulkanVersion { get; } = new();

    public ReactivePropertySlim<string?> AvailableMemory { get; } = new();

    public TelemetrySettingsPageViewModel Telemetry => _telemetry ??= new();

    public AsyncReactiveCommand NavigateToTelemetry { get; }
}
