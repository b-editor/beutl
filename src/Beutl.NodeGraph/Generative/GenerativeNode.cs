using System.ComponentModel.DataAnnotations;
using Beutl.Collections;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph.Composition;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Generative;

public enum GenerativeNodeStatus
{
    /// <summary>Nothing queued; the node shows its active generation, if any.</summary>
    Idle,
    /// <summary>Waiting for the nodes it depends on to finish.</summary>
    Queued,
    Running,
    Failed,
    /// <summary>An upstream generation failed, so this one could not run.</summary>
    Blocked,
    Canceled,
}

/// <summary>
/// A node whose output comes from a paid, asynchronous generation. It never generates
/// while the graph is evaluated: frames only ever show the active generation, which is
/// a file saved with the project. New generations run when they are queued.
/// </summary>
public abstract partial class GenerativeNode : GraphNode
{
    public static readonly CoreProperty<HierarchicalList<GenerationRecord>> GenerationsProperty;
    public static readonly CoreProperty<Guid> ActiveGenerationIdProperty;
    public static readonly CoreProperty<string> RequestKeySeedProperty;

    private readonly HierarchicalList<GenerationRecord> _generations;
    private Guid _activeGenerationId;
    private ActiveSnapshot? _active;
    private volatile string? _currentParameterFingerprint;
    private readonly object _previewLock = new();
    private int _previewLoadVersion;
    private Guid _previewShownFor;
    private NodeMonitor<Ref<Bitmap>?>? _previewMonitor;
    private NodeMonitor<string?>? _statusMonitor;

    static GenerativeNode()
    {
        GenerationsProperty = ConfigureProperty<HierarchicalList<GenerationRecord>, GenerativeNode>(nameof(Generations))
            .Accessor(o => o.Generations, (o, v) => o.Generations.Replace(v))
            .Register();
        ActiveGenerationIdProperty = ConfigureProperty<Guid, GenerativeNode>(nameof(ActiveGenerationId))
            .Accessor(o => o.ActiveGenerationId, (o, v) => o.ActiveGenerationId = v)
            .Register();
        RequestKeySeedProperty = ConfigureProperty<string, GenerativeNode>(nameof(RequestKeySeed))
            .Accessor(o => o.RequestKeySeed, (o, v) => o.RequestKeySeed = v)
            .DefaultValue(string.Empty)
            .Register();
    }

    protected GenerativeNode()
    {
        _generations = new HierarchicalList<GenerationRecord>(this);
        _generations.CollectionChanged += (_, _) => RefreshActive();
        RequestKeySeed = Guid.NewGuid().ToString("N");
    }

    public abstract GenerativeOperation Operation { get; }

    /// <summary>Every generation this node has kept, oldest first.</summary>
    [NotAutoSerialized]
    public HierarchicalList<GenerationRecord> Generations => _generations;

    [NotAutoSerialized]
    public Guid ActiveGenerationId
    {
        get => _activeGenerationId;
        set
        {
            if (SetAndRaise(ActiveGenerationIdProperty, ref _activeGenerationId, value))
                RefreshActive();
        }
    }

    /// <summary>The generation the node outputs.</summary>
    public GenerationRecord? ActiveGeneration
        => _generations.FirstOrDefault(record => record.Id == _activeGenerationId);

    /// <summary>
    /// Stable for the life of the node and saved with it, so a request that never
    /// reported back is asked for again under the same idempotency key after a restart.
    /// </summary>
    [NotAutoSerialized]
    public string RequestKeySeed
    {
        get;
        set => SetAndRaise(RequestKeySeedProperty, ref field, value);
    } = string.Empty;

    public GenerativeNodeStatus Status { get; private set; }

    public string? StatusMessage { get; private set; }

    /// <summary>
    /// True when the node's current inputs no longer describe its active generation,
    /// or when it has none. Queueing the node would generate again.
    /// </summary>
    public bool IsStale
    {
        get
        {
            string? current = _currentParameterFingerprint;
            ActiveSnapshot? active = Volatile.Read(ref _active);
            return active is null || (current is not null && current != active.ParameterFingerprint);
        }
    }

    public event EventHandler? StatusChanged;

    protected NodeMonitor<Ref<Bitmap>?> PreviewMonitor
        => _previewMonitor ?? throw new InvalidOperationException("Call AddGenerativeMonitors first.");

    /// <summary>Adds the preview and status monitors; call it after the node's ports.</summary>
    protected void AddGenerativeMonitors(DisplayAttribute? previewDisplay, DisplayAttribute? statusDisplay)
    {
        _previewMonitor = AddImageMonitor("GenerationPreview", previewDisplay);
        _statusMonitor = AddTextMonitor("GenerationStatus", statusDisplay);
    }

    /// <summary>
    /// The operation whose models the node offers, as the server names it. Edit nodes
    /// change it with their task, since each task registers its own models.
    /// </summary>
    public abstract string CatalogOperationId { get; }

    /// <summary>Raised when <see cref="CatalogOperationId"/> changes.</summary>
    public event EventHandler? CatalogOperationChanged;

    protected void RaiseCatalogOperationChanged() => CatalogOperationChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>The input that names the model, when the node has one.</summary>
    public virtual IPropertyAdapter<string>? ModelProperty => null;

    /// <summary>Marks a text input as a choice from the model catalog.</summary>
    protected void RegisterChoice(INodeMember port, GenerativeChoiceKind kind)
    {
        if (port.Property is { } property)
            GenerativeChoices.Register(property, new GenerativeChoice(this, kind));
    }

    /// <summary>
    /// Turns the evaluated inputs into a request. Called by the runner on the render
    /// thread right after the graph was evaluated, while <paramref name="resource"/>'s
    /// values are live. Throws <see cref="GenerativeExecutionException"/> for inputs
    /// that cannot be sent.
    /// </summary>
    protected internal abstract GenerativeRequest BuildRequest(GraphNode.Resource resource, GraphCompositionContext context);

    /// <summary>Runs on the UI thread after a generation became active.</summary>
    protected internal virtual void OnGenerated(GenerationRecord record)
    {
    }

    internal void SetStatus(GenerativeNodeStatus status, string? message = null)
    {
        Status = status;
        StatusMessage = message;
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
        if (Status == GenerativeNodeStatus.Running)
        {
            _previewMonitor.SetBusy(true);
            return;
        }

        Guid activeId = Volatile.Read(ref _active)?.Id ?? Guid.Empty;
        bool loading;
        lock (_previewLock)
            loading = activeId != Guid.Empty && _previewShownFor != activeId;
        _previewMonitor.SetBusy(loading, loading ? NodeGraphStrings.Generative_Loading : null);
    }

    /// <summary>Makes a kept generation the one the node outputs.</summary>
    public void SelectGeneration(Guid id)
    {
        if (_generations.Any(record => record.Id == id))
            ActiveGenerationId = id;
    }

    /// <summary>
    /// Removes generations that are neither pinned nor active. Their files stay on disk,
    /// because undoing the removal brings the records back.
    /// </summary>
    public int PruneGenerations()
    {
        GenerationRecord[] removable = _generations
            .Where(record => !record.IsPinned && record.Id != _activeGenerationId)
            .ToArray();
        foreach (GenerationRecord record in removable)
            _generations.Remove(record);
        return removable.Length;
    }

    /// <summary>Adds a finished generation and makes it the one the node outputs.</summary>
    internal GenerationRecord AddGeneration(GenerativeRequest request, GenerativeExecutionResult result)
    {
        var record = new GenerationRecord
        {
            Fingerprint = request.Fingerprint,
            ParameterFingerprint = request.ParameterFingerprint,
            ModelId = result.ModelId,
            Seed = result.Seed,
            Summary = request.Summary,
            CreatedAt = DateTimeOffset.Now,
        };
        if (result.IsVideo)
        {
            var video = new VideoSource();
            video.ReadFrom(result.ResultFile);
            record.Video = video;
        }
        else
        {
            var image = new ImageSource();
            image.ReadFrom(result.ResultFile);
            record.Image = image;
        }

        _generations.Add(record);
        ActiveGenerationId = record.Id;
        return record;
    }

    /// <summary>Called from the resource with the fingerprint of the current scalar inputs.</summary>
    protected void ReportParameterFingerprint(string fingerprint)
    {
        if (_currentParameterFingerprint == fingerprint)
            return;
        bool wasStale = IsStale;
        _currentParameterFingerprint = fingerprint;
        if (wasStale != IsStale)
        {
            _statusMonitor?.Value = FormatStatus();
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The picture the node outputs, safe to read from the render thread.</summary>
    protected ImageSource? ActiveImage => Volatile.Read(ref _active)?.Media as ImageSource;

    /// <summary>The clip the node outputs, safe to read from the render thread.</summary>
    protected VideoSource? ActiveVideo => Volatile.Read(ref _active)?.Media as VideoSource;

    private void RefreshActive()
    {
        GenerationRecord? record = ActiveGeneration;
        Volatile.Write(
            ref _active,
            record is not null && (record.Image ?? (MediaSource?)record.Video) is { } media
                ? new ActiveSnapshot(record.Id, media, record.ParameterFingerprint)
                : null);
        LoadActivePreview();
        UpdateBusy();
        _statusMonitor?.Value = FormatStatus();
        StatusChanged?.Invoke(this, EventArgs.Empty);
        // The output changed: without this the scene is not rendered again, and neither the
        // canvas nor anything downstream would show the new result.
        RaiseEdited();
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

    /// <summary>
    /// Renders a picture input the way the preview does and encodes it for upload.
    /// Returns null when there is nothing to draw.
    /// </summary>
    protected static GenerativeImageInput? RasterizeInput(RenderNode? node, string name, GraphCompositionContext context)
    {
        using Bitmap? bitmap = Rasterize(node, context);
        if (bitmap is null)
            return null;

        using var stream = new MemoryStream();
        if (!bitmap.Save(stream, EncodedImageFormat.Png))
            throw new GenerativeExecutionException(NodeGraphStrings.Generative_InputRenderFailed);
        return new GenerativeImageInput($"{name}.png", stream.ToArray());
    }

    private static Bitmap? Rasterize(RenderNode? node, GraphCompositionContext context)
    {
        if (node is null)
            return null;

        try
        {
            using var renderer = new RenderNodeRenderer(
                node,
                new RenderNodeRenderRequest { Intent = RenderIntent.Preview, ManageCacheLifecycle = false });
            using RenderNodeRasterization rasterization = renderer.Rasterize();
            return rasterization.Bitmap?.Clone();
        }
        catch (RenderTargetDomainRequiredException) when (context.TargetDomain is { } domain)
        {
            using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = domain,
                ManageCacheLifecycle = false,
            });
            using RenderNodeRasterization rasterization = renderer.Rasterize();
            return rasterization.Bitmap?.Clone();
        }
    }

    /// <summary>Reads a clip handed to a generation as input.</summary>
    protected static GenerativeFileInput? ReadVideoInput(VideoSource? source, string name)
    {
        if (source is not { HasUri: true } || !source.Uri.IsFile)
            return null;

        string path = source.Uri.LocalPath;
        string extension = Path.GetExtension(path).ToLowerInvariant();
        string mediaType = extension switch
        {
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            _ => throw new GenerativeExecutionException(Strings.AiVideoInputUnavailable),
        };
        try
        {
            return new GenerativeFileInput($"{name}{extension}", mediaType, File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GenerativeExecutionException(Strings.AiVideoInputUnavailable, ex);
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(RequestKeySeed), RequestKeySeed);
        if (_generations.Count > 0)
            context.SetValue(nameof(Generations), _generations);
        if (_activeGenerationId != Guid.Empty)
            context.SetValue(nameof(ActiveGenerationId), _activeGenerationId);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<string>(nameof(RequestKeySeed)) is { Length: > 0 } seed)
            RequestKeySeed = seed;
        if (context.GetValue<GenerationRecord[]>(nameof(Generations)) is { } generations)
            _generations.Replace(generations);
        if (context.Contains(nameof(ActiveGenerationId)))
            ActiveGenerationId = context.GetValue<Guid>(nameof(ActiveGenerationId));
    }

    private sealed record ActiveSnapshot(Guid Id, MediaSource Media, string ParameterFingerprint);

    public partial class Resource
    {
        private ImageSourceRenderNode? _cachedOutput;
        private ImageSource.Resource? _sourceResource;
        private ImageSource? _lastSource;

        /// <summary>The active generation as a render node, or null when there is none.</summary>
        protected ImageSourceRenderNode? UpdateActiveImageOutput(GraphCompositionContext context)
        {
            ImageSource? source = RequireOriginal().ActiveImage;
            if (source is null)
            {
                ReleaseOutput();
                return null;
            }

            if (!ReferenceEquals(_lastSource, source))
            {
                _sourceResource?.Dispose();
                _sourceResource = source.ToResource(context);
                _lastSource = source;
            }
            else
            {
                bool updateOnly = false;
                _sourceResource!.Update(source, context, ref updateOnly);
            }

            if (_cachedOutput is null)
                _cachedOutput = new ImageSourceRenderNode(_sourceResource, Brushes.Resource.White, null);
            else
                _cachedOutput.Update(_sourceResource, Brushes.Resource.White, null);
            return _cachedOutput;
        }

        private VideoSource.Resource? _videoResource;
        private VideoSource? _lastVideo;

        /// <summary>The active clip.</summary>
        protected VideoSource? UpdateActiveVideoOutput(GraphCompositionContext context)
        {
            GenerativeNode node = RequireOriginal();
            VideoSource? source = node.ActiveVideo;
            if (source is null)
            {
                ReleaseVideo();
                return null;
            }

            if (!ReferenceEquals(_lastVideo, source))
            {
                ReleaseVideo();
                _videoResource = source.ToResource(context);
                _lastVideo = source;
            }
            else
            {
                bool updateOnly = false;
                _videoResource!.Update(source, context, ref updateOnly);
            }

            return source;
        }

        private void ReleaseVideo()
        {
            _videoResource?.Dispose();
            _videoResource = null;
            _lastVideo = null;
        }

        private void ReleaseOutput()
        {
            _cachedOutput?.Dispose();
            _cachedOutput = null;
            _sourceResource?.Dispose();
            _sourceResource = null;
            _lastSource = null;
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                ReleaseOutput();
                ReleaseVideo();
            }
        }
    }
}
