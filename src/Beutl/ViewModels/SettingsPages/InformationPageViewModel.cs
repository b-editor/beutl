using System.Reflection;
using Avalonia.Controls;
using Beutl.Configuration;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Services;
using Beutl.Threading;
using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class InformationPageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    public InformationPageViewModel(UpdateConfig updateConfig)
    {
        AutoDownloadUpdates = updateConfig.AutoDownload.GetObservable()
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        AutoDownloadUpdates.Skip(1)
            .Subscribe(value => updateConfig.AutoDownload.Value = value)
            .DisposeWith(_disposables);

        RenderThread.Dispatcher.Dispatch(() =>
        {
            if (!Design.IsDesignMode)
            {
                GraphicsContextFactory.GetOrCreateShared();

                var devices = GraphicsContextFactory.GetAvailableDevices();
                AvailableGpus.Value = devices
                    .Select(d => $"{d.Name} ({d.DeviceType})")
                    .ToArray();

                if (GraphicsContextFactory.GetSelectedDevice() is { } selectedDevice)
                {
                    SelectedGpu.Value = $"{selectedDevice.Name} ({selectedDevice.DeviceType})";

                    VulkanVersion.Value = selectedDevice.ApiVersion;

                    AvailableMemory.Value = $"Total: {selectedDevice.TotalMemoryMB:N0} MB";
                }

                EnabledExtensions.Value = [.. GraphicsContextFactory.GetEnabledExtensions()];
            }
        }, DispatchPriority.Low);

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

    public ReactiveProperty<bool> AutoDownloadUpdates { get; }

    public bool CanDownloadUpdatesAutomatically => AppUpdateController.CanDownloadAutomatically;

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
