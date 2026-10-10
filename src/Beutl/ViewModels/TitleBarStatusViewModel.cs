using Avalonia.Controls;
using Avalonia.Threading;
using Beutl.AgentHost;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Models;
using Beutl.Services;
using Beutl.Utilities;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public sealed class TitleBarStatusViewModel : IDisposable
{
    private static readonly TimeSpan s_refreshInterval = TimeSpan.FromSeconds(1);
    private readonly ILogger _logger = Log.CreateLogger<TitleBarStatusViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly EditorService _editorService;
    private readonly AgentHostEndpoint _agentHostEndpoint;
    private readonly Action _openAiJobCenter;
    private DispatcherTimer? _refreshTimer;
    private bool _graphicsRequested;
    private bool _isSamplingFrameCache;

    internal TitleBarStatusViewModel(
        IObservable<bool> isRunningStartupTasks,
        EditorService editorService,
        AgentHostEndpoint agentHostEndpoint,
        IObservable<AuthenticatedUser?> authenticatedUser,
        IObservable<AiJobMonitorSnapshot> aiJobs,
        IAiJobKindRegistry aiJobKinds,
        Action openAiJobCenter)
    {
        _editorService = editorService;
        _agentHostEndpoint = agentHostEndpoint;
        _openAiJobCenter = openAiJobCenter;

        // The sources publish from background threads, so every one is moved to the UI thread here.
        IsRunningStartupTasks = isRunningStartupTasks
            .ObserveOnUIDispatcher()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        RunningAiJobCount = aiJobs
            .Select(snapshot => CountRunningJobs(snapshot, aiJobKinds))
            .ObserveOnUIDispatcher()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        RunningOutputs = editorService.RunningOutputs
            .Select(items => items.Select(DescribeOutput).ToArray())
            .ObserveOnUIDispatcher()
            .ToReadOnlyReactivePropertySlim([])
            .DisposeWith(_disposables);

        IsBusy = IsRunningStartupTasks
            .CombineLatest(RunningAiJobCount, RunningOutputs,
                (startup, jobs, outputs) => startup || jobs > 0 || outputs.Length > 0)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        ToolTip = IsRunningStartupTasks
            .CombineLatest(RunningAiJobCount, RunningOutputs, BuildToolTip)
            .ToReadOnlyReactivePropertySlim(StatusStrings.Title)
            .DisposeWith(_disposables);

        StartupText = IsRunningStartupTasks
            .Select(running => running ? StatusStrings.Running : StatusStrings.Done)
            .ToReadOnlyReactivePropertySlim(StatusStrings.Done)
            .DisposeWith(_disposables);

        AiJobsText = RunningAiJobCount
            .Select(count => count > 0 ? string.Format(StatusStrings.RunningCount, count) : StatusStrings.None)
            .ToReadOnlyReactivePropertySlim(StatusStrings.None)
            .DisposeWith(_disposables);

        HasRunningOutputs = RunningOutputs
            .Select(outputs => outputs.Length > 0)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        // A profile refresh renames the user in place and then publishes a new response.
        AccountText = authenticatedUser
            .Select(user => user is null
                ? Observable.Return(StatusStrings.NotSignedIn)
                : user.Profile.Response.Select(_ => user.Profile.Name))
            .Switch()
            .ObserveOnUIDispatcher()
            .ToReadOnlyReactivePropertySlim(StatusStrings.NotSignedIn)
            .DisposeWith(_disposables);
    }

    public ReadOnlyReactivePropertySlim<bool> IsBusy { get; }

    public ReadOnlyReactivePropertySlim<string> ToolTip { get; }

    public ReadOnlyReactivePropertySlim<bool> IsRunningStartupTasks { get; }

    public ReadOnlyReactivePropertySlim<int> RunningAiJobCount { get; }

    public ReadOnlyReactivePropertySlim<string[]> RunningOutputs { get; }

    public ReadOnlyReactivePropertySlim<bool> HasRunningOutputs { get; }

    public ReadOnlyReactivePropertySlim<string> StartupText { get; }

    public ReadOnlyReactivePropertySlim<string> AiJobsText { get; }

    public ReadOnlyReactivePropertySlim<string> AccountText { get; }

    public ReactivePropertySlim<string> GpuText { get; } = new(StatusStrings.Loading);

    public ReactivePropertySlim<string> GraphicsApiText { get; } = new(StatusStrings.Loading);

    public ReactivePropertySlim<string> VideoMemoryText { get; } = new(StatusStrings.Loading);

    public ReactivePropertySlim<string> Rendering3DText { get; } = new(StatusStrings.Loading);

    public ReactivePropertySlim<bool> IsRendering3DUnavailable { get; } = new();

    public ReactivePropertySlim<string> MaxTextureSizeText { get; } = new(StatusStrings.Loading);

    public ReactivePropertySlim<bool> IsGraphicsUnavailable { get; } = new();

    public ReactivePropertySlim<string> ProcessMemoryText { get; } = new(StatusStrings.Loading);

    public ReactivePropertySlim<string> ManagedHeapText { get; } = new(StatusStrings.Loading);

    public ReactivePropertySlim<string> FrameCacheText { get; } = new(StatusStrings.Loading);

    public ReactivePropertySlim<string> LiveMcpText { get; } = new(StatusStrings.Stopped);

    public ReactivePropertySlim<string?> LiveMcpEndpoint { get; } = new();

    // Resource figures change continuously, so they are only sampled while the popup is open.
    public void OnPopupOpened()
    {
        RequestGraphicsStatus();
        RefreshSamples();

        if (_refreshTimer is null)
        {
            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = s_refreshInterval };
            _refreshTimer.Tick += (_, _) => RefreshSamples();
        }

        _refreshTimer.Start();
    }

    public void OnPopupClosed()
    {
        _refreshTimer?.Stop();
    }

    public void OpenAiJobCenter()
    {
        _openAiJobCenter();
    }

    public void Dispose()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;
        _disposables.Dispose();
    }

    private void RefreshSamples()
    {
        ProcessMemoryText.Value = StringFormats.ToHumanReadableSize(Environment.WorkingSet);
        ManagedHeapText.Value = StringFormats.ToHumanReadableSize(GC.GetTotalMemory(false));
        _ = SampleFrameCacheAsync();

        // The endpoint does not notify when it starts or stops, so it is sampled with the rest.
        Uri? endpoint = _agentHostEndpoint.IsRunning ? _agentHostEndpoint.EndpointUri : null;
        LiveMcpText.Value = endpoint is null ? StatusStrings.Stopped : StatusStrings.Running;
        LiveMcpEndpoint.Value = endpoint?.ToString();
    }

    // A cache is counted under the lock its frame conversions hold, so the count runs off the UI thread
    // to keep playback from stalling the popup.
    private async Task SampleFrameCacheAsync()
    {
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        if (!config.IsFrameCacheEnabled)
        {
            FrameCacheText.Value = StatusStrings.Disabled;
            return;
        }

        if (_isSamplingFrameCache)
            return;

        FrameCacheManager[] caches = _editorService.TabItems
            .Select(item => item.Context.Value)
            .OfType<EditViewModel>()
            .Select(editor => editor.FrameCacheManager.Value)
            .OfType<FrameCacheManager>()
            .ToArray();
        string limit = StringFormats.ToHumanReadableSize(config.FrameCacheMaxSize * 1024 * 1024);

        _isSamplingFrameCache = true;
        try
        {
            long used = await Task.Run(() => caches.Sum(cache => cache.CalculateByteCount(int.MinValue, int.MaxValue)));
            FrameCacheText.Value = string.Format(StatusStrings.FrameCacheUsage, StringFormats.ToHumanReadableSize(used), limit);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to measure the frame cache.");
        }
        finally
        {
            _isSamplingFrameCache = false;
        }
    }

    // The device does not change while the application runs, so it is read once, on the render thread
    // that owns the graphics context.
    private void RequestGraphicsStatus()
    {
        if (_graphicsRequested || Design.IsDesignMode)
            return;

        _graphicsRequested = true;
        RenderThread.Dispatcher.Dispatch(() =>
        {
            GraphicsStatus? status;
            try
            {
                status = ReadGraphicsStatus();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read the graphics status.");
                status = null;
            }

            Dispatcher.UIThread.Post(() => ApplyGraphicsStatus(status));
        }, Beutl.Threading.DispatchPriority.Low);
    }

    private static GraphicsStatus? ReadGraphicsStatus()
    {
        if (GraphicsContextFactory.GetOrCreateShared() is not { } context)
            return null;

        GraphicsDeviceInfo? device = GraphicsContextFactory.GetSelectedDevice();
        string api = context.Backend == GraphicsBackend.Vulkan
            ? $"Vulkan {device?.ApiVersion}"
            : $"{context.Backend} / Vulkan {device?.ApiVersion}";

        return new GraphicsStatus(
            device is null ? null : $"{device.Name} ({device.DeviceType})",
            api.TrimEnd(),
            device is { TotalMemoryMB: > 0 } ? StringFormats.ToHumanReadableSize(device.TotalMemoryMB * 1024d * 1024d) : null,
            context.Supports3DRendering,
            context.MaxAttachmentDimension > 0 ? $"{context.MaxAttachmentDimension} px" : null);
    }

    private void ApplyGraphicsStatus(GraphicsStatus? status)
    {
        IsGraphicsUnavailable.Value = status is null;
        GpuText.Value = status?.Device ?? "-";
        GraphicsApiText.Value = status?.Api ?? "-";
        VideoMemoryText.Value = status?.VideoMemory ?? "-";
        MaxTextureSizeText.Value = status?.MaxTextureSize ?? "-";
        bool supports3D = status?.Supports3DRendering ?? false;
        Rendering3DText.Value = supports3D ? StatusStrings.Available : StatusStrings.Unavailable;
        IsRendering3DUnavailable.Value = !supports3D;
    }

    private static string BuildToolTip(bool runningStartupTasks, int runningAiJobs, string[] runningOutputs)
    {
        List<string> lines = [StatusStrings.Title];
        if (runningStartupTasks)
            lines.Add(Strings.RunningStartupTasks);
        if (runningOutputs.Length > 0)
            lines.Add($"{Strings.Output}: {string.Format(StatusStrings.RunningCount, runningOutputs.Length)}");
        if (runningAiJobs > 0)
            lines.Add($"{Strings.AiJobCenter}: {string.Format(StatusStrings.RunningCount, runningAiJobs)}");

        return string.Join(Environment.NewLine, lines);
    }

    private static int CountRunningJobs(AiJobMonitorSnapshot snapshot, IAiJobKindRegistry jobKinds)
    {
        return snapshot.Jobs.Count(job =>
        {
            try
            {
                return jobKinds.GetStatus(job).ShouldPoll;
            }
            catch
            {
                // A job whose status cannot be resolved is not known to be running.
                return false;
            }
        });
    }

    private static string DescribeOutput(OutputProfileItem item)
    {
        string name = item.Context.Name.Value;
        string? file = item.Context.Object.Uri is { } uri ? Path.GetFileName(uri.LocalPath) : null;
        return string.IsNullOrEmpty(file) ? name : $"{file} - {name}";
    }

    private sealed record GraphicsStatus(
        string? Device,
        string Api,
        string? VideoMemory,
        bool Supports3DRendering,
        string? MaxTextureSize);
}
