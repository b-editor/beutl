using System.ComponentModel.DataAnnotations;
using Beutl.Collections;
using Beutl.Extensibility;
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

    /// <summary>
    /// The request for the <paramref name="index"/>th of several variations queued at once.
    /// Nodes with a seed move it on by the index; others send the same request, which the
    /// renewed idempotency key after each result turns into a new generation.
    /// </summary>
    protected internal virtual GenerativeRequest BuildVariation(
        GraphNode.Resource resource,
        GraphCompositionContext context,
        int index)
        => BuildRequest(resource, context);

    /// <summary>
    /// Sets the node's inputs to reproduce <paramref name="request"/>, so a variation that was
    /// kept does not read as stale. Runs on the UI thread.
    /// </summary>
    protected internal virtual void ApplyRequestInputs(GenerativeRequest request)
    {
    }

    /// <summary>Sets the node's inputs, as far as the record says, to reproduce a kept result.</summary>
    protected internal virtual void ApplyRecordInputs(GenerationRecord record)
    {
    }

    /// <summary>Runs on the UI thread after a generation became active.</summary>
    protected internal virtual void OnGenerated(GenerationRecord record)
    {
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

    /// <summary>
    /// Starts a new idempotency key once the last one is settled. The key is derived from the
    /// seed and the request, so without this an identical request — a regeneration, or a retry
    /// after a refunded failure — would be answered with the old job instead of a new one.
    /// </summary>
    internal void RenewRequestKey() => RequestKeySeed = Guid.NewGuid().ToString("N");

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
