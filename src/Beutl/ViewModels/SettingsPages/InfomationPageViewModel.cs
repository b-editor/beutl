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
                GraphicsContextFactory.GetOrCreateShared();

                // Available GPUs
                var devices = GraphicsContextFactory.GetAvailableDevices();
                AvailableGpus.Value = devices
                    .Select(d => $"{d.Name} ({d.DeviceType})")
                    .ToArray();

                // Selected GPU details
                if (GraphicsContextFactory.GetSelectedDevice() is { } selectedDevice)
                {
                    SelectedGpu.Value = $"{selectedDevice.Name} ({selectedDevice.DeviceType})";

                    // Vulkan version
                    VulkanVersion.Value = selectedDevice.ApiVersion;

                    // Memory info
                    AvailableMemory.Value = $"Total: {selectedDevice.TotalMemoryMB:N0} MB";
                }

                // Enabled extensions
                EnabledExtensions.Value = [.. GraphicsContextFactory.GetEnabledExtensions()];
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
