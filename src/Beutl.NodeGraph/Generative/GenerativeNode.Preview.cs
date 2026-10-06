using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.NodeGraph.Generative;

public abstract partial class GenerativeNode
{
    internal void SetStatus(GenerativeNodeStatus status, string? message = null)
    {
        lock (_previewLock)
        {
            Status = status;
            StatusMessage = message;
        }

        UpdateBusy();
        _statusMonitor?.Value = FormatStatus();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows a picture in the preview monitor; the monitor takes ownership.</summary>
    internal void ShowPreview(Ref<Bitmap>? preview) => SwapPreview(preview, Guid.Empty);

    private void SwapPreview(Ref<Bitmap>? preview, Guid shownFor)
    {
        if (_previewMonitor is null)
        {
            preview?.Dispose();
            return;
        }

        Ref<Bitmap>? previous;
        lock (_previewLock)
        {
            previous = _previewMonitor.Value;
            _previewMonitor.Value = preview;
            _previewShownFor = shownFor;
        }

        previous?.Dispose();
        UpdateBusy();
    }

    /// <summary>
    /// A ring while generating, and "loading" while the active generation has not reached
    /// the preview yet — after opening a project or switching to another kept result.
    /// </summary>
    private void UpdateBusy()
    {
        if (_previewMonitor is null)
            return;

        // Under the lock the status is set under: a preview decoded off the UI thread must not
        // publish a busy state computed from a status that has since changed.
        lock (_previewLock)
        {
            if (Status == GenerativeNodeStatus.Running)
            {
                _previewMonitor.SetBusy(true);
                return;
            }

            Guid activeId = Volatile.Read(ref _active)?.Id ?? Guid.Empty;
            bool loading = activeId != Guid.Empty && _previewShownFor != activeId;
            _previewMonitor.SetBusy(loading, loading ? NodeGraphStrings.Generative_Loading : null);
        }
    }

    /// <summary>The preview load in flight, for tests.</summary>
    internal Task PreviewLoad { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Shows the active result in the preview again, replacing a streamed or cleared preview.
    /// </summary>
    internal void RestoreActivePreview() => LoadActivePreview();

    /// <summary>
    /// Decodes the active result for the preview straight from its file, off the UI thread.
    /// The preview does not wait for the graph to be evaluated, which only happens while the
    /// graph's element is rendered at the current time.
    /// </summary>
    private void LoadActivePreview()
    {
        int version = Interlocked.Increment(ref _previewLoadVersion);
        ActiveSnapshot? active = Volatile.Read(ref _active);
        if (_previewMonitor is null)
            return;
        if (active is null)
        {
            SwapPreview(null, Guid.Empty);
            return;
        }

        lock (_previewLock)
        {
            if (_previewShownFor == active.Id && _previewMonitor.Value is not null)
                return;
        }

        PreviewLoad = Task.Run(() =>
        {
            Bitmap? bitmap = null;
            try
            {
                bitmap = DecodePreview(active.Media);
            }
            catch (Exception)
            {
                // A missing or unreadable file shows no picture; it must not leave the node loading.
            }

            if (Volatile.Read(ref _previewLoadVersion) != version)
            {
                bitmap?.Dispose();
                return;
            }

            SwapPreview(bitmap is null ? null : Ref<Bitmap>.Create(bitmap), active.Id);
        });
    }

    /// <summary>Decodes a kept result for display, or null when its file cannot be read.</summary>
    internal static Bitmap? DecodeThumbnail(GenerationRecord record)
    {
        try
        {
            return (record.Image ?? (MediaSource?)record.Video) is { } media ? DecodePreview(media) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Bitmap? DecodePreview(MediaSource media)
    {
        if (!media.HasUri || !media.Uri.IsFile || !File.Exists(media.Uri.LocalPath))
            return null;

        string path = media.Uri.LocalPath;
        if (media is ImageSource)
            return Bitmap.FromFile(path);

        using var reader = Beutl.Media.Decoding.MediaReader.Open(
            path,
            new Beutl.Media.Decoding.MediaOptions(Beutl.Media.Decoding.MediaMode.Video));
        if (!reader.ReadVideo(0, out Ref<Bitmap>? frame))
            return null;
        using (frame)
            return frame.Value.Clone();
    }

    private string? FormatStatus()
    {
        return Status switch
        {
            GenerativeNodeStatus.Idle => IsStale ? NodeGraphStrings.Generative_Stale : null,
            GenerativeNodeStatus.Queued => NodeGraphStrings.Generative_Queued,
            GenerativeNodeStatus.Running => StatusMessage ?? NodeGraphStrings.Generative_Running,
            GenerativeNodeStatus.Failed => StatusMessage ?? NodeGraphStrings.Generative_Failed,
            GenerativeNodeStatus.Blocked => NodeGraphStrings.Generative_Blocked,
            GenerativeNodeStatus.Canceled => NodeGraphStrings.Generative_Canceled,
            _ => null,
        };
    }
}
