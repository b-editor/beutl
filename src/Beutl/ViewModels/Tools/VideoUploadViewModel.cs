using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using Beutl.Api.Services;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Tools;

public sealed class VideoUploadViewModel : IDisposable
{
    private readonly IExtensionProvider _extensions;
    private readonly CompositeDisposable _subscriptions = [];
    private IExtensionLease<VideoUploadExtension>? _contextLease;
    private IVideoUploadContext? _context;
    private IDisposable? _readySubscription;
    private JsonObject _settings = new();
    private string? _selectedTypeName;
    private bool _disposed;

    public VideoUploadViewModel(IExtensionProvider extensions)
    {
        _extensions = extensions;
        SelectedTarget.Subscribe(ChangeTarget).DisposeWith(_subscriptions);
        CanUpload = IsReady.CombineLatest(CompletedFile, IsUploading,
                (ready, file, uploading) => ready && file != null && !uploading)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_subscriptions);
        CanAutoUpload = Automatic.CombineLatest(IsReady, (automatic, ready) => !automatic || ready)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_subscriptions);
        _extensions.ExtensionsChanged += OnExtensionsChanged;
        RefreshTargets();
    }

    public ObservableCollection<UploadTarget> Targets { get; } = [];
    public ReactivePropertySlim<UploadTarget?> SelectedTarget { get; } = new();
    public ReactivePropertySlim<Control?> SettingsControl { get; } = new();
    public ReactivePropertySlim<bool> Automatic { get; } = new();
    public ReactivePropertySlim<bool> IsReady { get; } = new();
    public ReactivePropertySlim<bool> IsUploading { get; } = new();
    public ReactivePropertySlim<string?> CompletedFile { get; } = new();
    public ReactivePropertySlim<string?> UploadedUrl { get; } = new();
    public ReactivePropertySlim<double> Progress { get; } = new();
    public ReactivePropertySlim<string> Status { get; } = new("");
    public ReadOnlyReactivePropertySlim<bool> CanUpload { get; }
    public ReadOnlyReactivePropertySlim<bool> CanAutoUpload { get; }

    private void OnExtensionsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) RefreshTargets();
        else Dispatcher.UIThread.Post(RefreshTargets);
    }

    private void RefreshTargets()
    {
        if (_disposed) return;
        var targets = new List<UploadTarget>();
        foreach (ExtensionDescriptor descriptor in _extensions.GetDescriptors<VideoUploadExtension>())
        {
            if (!_extensions.TryAcquire<VideoUploadExtension>(descriptor.Id, out var lease)) continue;
            using (lease)
            {
                string? name = ExtensionGuard.Call(lease.Extension, () => lease.Extension.DisplayName);
                if (name != null) targets.Add(new(descriptor.Id, descriptor.TypeName, name));
            }
        }

        UploadTarget? selected = targets.FirstOrDefault(x => x.TypeName == _selectedTypeName);
        if (_selectedTypeName == null) selected = targets.FirstOrDefault();
        // Keep an active connection when an unrelated extension is installed or removed.
        foreach (UploadTarget target in Targets.ToArray())
            if (!targets.Contains(target)) Targets.Remove(target);
        foreach (UploadTarget target in targets)
            if (!Targets.Contains(target)) Targets.Add(target);
        SelectedTarget.Value = selected;
    }

    private void SaveContext()
    {
        if (_context != null && _contextLease != null)
        {
            ExtensionGuard.Call(_contextLease.Extension, () =>
            {
                var json = new JsonObject();
                _context.WriteToJson(json);
                _settings[_selectedTypeName!] = json;
                return true;
            });
        }
    }

    private void ChangeTarget(UploadTarget? target)
    {
        SaveContext();
        ReleaseContext();
        if (target == null) return;

        _selectedTypeName = target.TypeName;
        if (!_extensions.TryAcquire<VideoUploadExtension>(target.Id, out var lease)) return;
        _contextLease = lease;
        if (!ExtensionGuard.Call(lease.Extension, () =>
            {
                _context = lease.Extension.CreateContext();
                if (_settings[target.TypeName] is JsonObject settings)
                    _context.ReadFromJson((JsonObject)settings.DeepClone());
                SettingsControl.Value = lease.Extension.CreateControl(_context);
                _readySubscription = _context.IsReady.Subscribe(value => IsReady.Value = value);
                return true;
            }))
        {
            ReleaseContext();
            Status.Value = Strings.VideoUpload_NotReady;
        }
    }

    private void ReleaseContext()
    {
        _readySubscription?.Dispose();
        _readySubscription = null;
        SettingsControl.Value = null;
        IVideoUploadContext? context = _context;
        IExtensionLease<VideoUploadExtension>? lease = _contextLease;
        _context = null;
        _contextLease = null;
        IsReady.Value = false;
        try { context?.Dispose(); }
        finally { lease?.Dispose(); }
    }

    internal UploadJob CreateUpload(string filePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsReady.Value || _context == null || SelectedTarget.Value is not { } target
            || !_extensions.TryAcquire<VideoUploadExtension>(target.Id, out var lease))
            throw new InvalidOperationException(Strings.VideoUpload_NotReady);

        try
        {
            string fullPath = Path.GetFullPath(filePath);
            return new UploadJob(fullPath, _context.CreateUpload(fullPath), lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    internal void RecordCompletedFile(string? filePath)
    {
        CompletedFile.Value = filePath == null ? null : Path.GetFullPath(filePath);
        UploadedUrl.Value = null;
        Progress.Value = 0;
        Status.Value = "";
    }

    internal async Task RunAsync(UploadJob job, CancellationToken cancellationToken)
    {
        IsUploading.Value = true;
        UploadedUrl.Value = null;
        Progress.Value = 0;
        Status.Value = Strings.VideoUpload_Uploading;
        // Ignore callbacks queued by a provider after this particular run has finished.
        bool active = true;
        var progress = new Progress<double>(value =>
        {
            if (active && double.IsFinite(value)) Progress.Value = Math.Clamp(value, 0, 1);
        });
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(job.FilePath)) throw new FileNotFoundException(Strings.VideoUpload_FileMissing);
            Uri? result = await job.Operation.UploadAsync(progress, cancellationToken);
            UploadedUrl.Value = result?.IsAbsoluteUri == true ? result.AbsoluteUri : null;
            Progress.Value = 1;
            Status.Value = Strings.VideoUpload_Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status.Value = Strings.VideoUpload_Cancelled;
            throw;
        }
        catch (Exception ex)
        {
            Status.Value = $"{Strings.VideoUpload_Failed}\n{ex.Message}";
            Services.NotificationService.ShowError(Strings.VideoUpload_Title, Status.Value);
            OutputViewModel.MarkFailureAsReported(ex);
            throw;
        }
        finally
        {
            active = false;
            IsUploading.Value = false;
        }
    }

    public void WriteToJson(JsonObject json)
    {
        SaveContext();
        json["Target"] = _selectedTypeName;
        json["Settings"] = _settings.DeepClone();
        json[nameof(Automatic)] = Automatic.Value;
        json[nameof(CompletedFile)] = CompletedFile.Value;
        json[nameof(UploadedUrl)] = UploadedUrl.Value;
    }

    public void ReadFromJson(JsonObject json)
    {
        SelectedTarget.Value = null;
        _selectedTypeName = json["Target"]?.GetValue<string>();
        _settings = json["Settings"] is JsonObject settings ? (JsonObject)settings.DeepClone() : new();
        Automatic.Value = json[nameof(Automatic)]?.GetValue<bool>() ?? false;
        CompletedFile.Value = json[nameof(CompletedFile)]?.GetValue<string>();
        UploadedUrl.Value = json[nameof(UploadedUrl)]?.GetValue<string>();
        Status.Value = UploadedUrl.Value != null ? Strings.VideoUpload_Completed : "";
        RefreshTargets();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _extensions.ExtensionsChanged -= OnExtensionsChanged;
        try { ReleaseContext(); }
        finally { _subscriptions.Dispose(); }
    }

    public sealed record UploadTarget(ExtensionId Id, string TypeName, string DisplayName);

    internal sealed class UploadJob(
        string filePath, IVideoUploadOperation operation, IExtensionLease<VideoUploadExtension> lease) : IDisposable
    {
        public string FilePath { get; } = filePath;
        public IVideoUploadOperation Operation { get; } = operation;

        public void Dispose()
        {
            try { Operation.Dispose(); }
            finally { lease.Dispose(); }
        }
    }
}
