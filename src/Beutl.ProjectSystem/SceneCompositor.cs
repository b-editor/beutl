using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transitions;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed partial class SceneCompositor : ICompositor
{
    private readonly ConditionalWeakTable<EngineObject, ResourceEntry> _resourceCache = new();
    private readonly ConcurrentQueue<EngineObject.Resource> _detachedResources = new();
    private readonly object _resourceCacheLock = new();
    // Sample sessions retain only resources used by their current evaluation. Ordinary frame caches
    // keep their existing lifetime; the session owns this set and releases unused entries afterward.
    private HashSet<EngineObject.Resource>? _usedSampleResources;
    // One presenter per transition draws it for this compositor; it lives as long as the transition does.
    private readonly ConditionalWeakTable<ClipTransition, ClipTransitionPresenter> _transitionPresenters = new();

    // Mute flags are read live from the layers inside the snapshot, so only the
    // lookup shape (membership, ZIndex) and HasSolo require invalidation.
    private volatile SceneLayerSnapshot? _layerSnapshot;
    // The elements that can take part in a transition, kept until the scene is edited. The version moves
    // on every edit, so a set built while an edit lands is not kept past it.
    private volatile ParticipantSnapshot? _participants;
    private int _participantsVersion;
    private volatile AudioEligibilitySnapshot? _audioEligibility;
    private int _audioEligibilityVersion;

    public SceneCompositor(Scene scene, SceneRenderServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Services = services;
        Scene = scene;
        _layerQueries = new(this, CreateLayerQuery);
        _audioQueries = new(this, CreateAudioQuery);
        Scene.Layers.CollectionChanged += OnLayersCollectionChanged;
        Scene.Edited += OnSceneEdited;
        Scene.Children.CollectionChanged += OnChildrenCollectionChanged;
        Scene.Layers.Attached += OnLayerAttached;
        Scene.Layers.Detached += OnLayerDetached;
        foreach (TimelineLayer layer in Scene.Layers)
        {
            ObserveLayer(layer);
        }
    }

    public SceneRenderServices Services { get; }

    public FontManager? Fonts => Services.Fonts;

    public DecoderRegistry? Decoders => Services.Decoders;

    public Scene Scene { get; }

    public bool DisableResourceShare { get; init; }

    public bool ForceOriginalSource { get; init; }

    /// <summary>Objects this compositor's frames evaluate as disabled; see <see cref="CompositionConditions.DisabledObjects"/>.</summary>
    /// <remarks>Set it before the first frame. Nested scenes inherit it with the rest of the conditions.</remarks>
    internal IReadOnlySet<EngineObject>? DisabledObjects { get; set; }

    /// <summary>Filter effects whose color stages this compositor's frames leave out; see <see cref="CompositionConditions.ColorStagesSkipped"/>.</summary>
    /// <remarks>Set it before the first frame. Nested scenes inherit it with the rest of the conditions.</remarks>
    internal IReadOnlySet<EngineObject>? ColorStagesSkipped { get; set; }

    // Replaces a compositor whose scene or sharing/source settings no longer match the context and
    // creates one for a referenced scene. The field is cleared before construction, so a constructor
    // that throws leaves no disposed compositor behind.
    internal static void Refresh(ref SceneCompositor? field, Scene? scene, CompositionContext context)
    {
        SceneCompositor? parent = (context as CompositorContext)?.Compositor
            ?? context.EvaluationServices?.SessionIdentity as SceneCompositor;
        SceneRenderServices services = (parent?.Services ?? SceneRenderServices.Empty) with
        {
            Fonts = context.Fonts,
            Decoders = context.Decoders,
        };
        bool forceOriginalSource = !context.PreferProxy;
        if (field?.Scene != scene
            || field?.DisableResourceShare != context.DisableResourceShare
            || field?.ForceOriginalSource != forceOriginalSource
            || field?.Services != services)
        {
            field?.Dispose();
            field = null;
        }

        if (scene != null && field == null)
        {
            field = new SceneCompositor(scene, services)
            {
                DisableResourceShare = context.DisableResourceShare,
                ForceOriginalSource = forceOriginalSource,
            };
        }
    }

    private sealed class CompositorContext : CompositionContext, ISceneCompositionContext
    {
        private readonly SceneCompositor _compositor;

        public SceneCompositor Compositor => _compositor;

        public CompositorContext(MediaTime time,
            SceneCompositor compositor,
            IList<EngineObject.Resource> flow,
            IList<Element> currentElements,
            CompositionTarget target, EvaluationServices? services = null) : base(time)
        {
            _compositor = compositor;
            CurrentElements = currentElements;
            Target = target;
            GraphicsState = target == CompositionTarget.Graphics ? new(time) : null;
            Flow = flow;
            DisableResourceShare = compositor.DisableResourceShare;
            Conditions = new CompositionConditions
            {
                Fonts = compositor.Fonts,
                Decoders = compositor.Decoders,
                PreferProxy = !compositor.ForceOriginalSource
                    && (compositor.Services.EditorConfig?.PreviewSourceMode.Value ?? PreviewSourceMode.PreferProxy) == PreviewSourceMode.PreferProxy,
                PreferredProxyPreset = ToPreset(compositor.Services.ProxyConfig?.DefaultPreset.Value ?? ProxyStoreConfig.DefaultPresetValue),
                TargetDomain = new Rect(default, compositor.Scene.FrameSize.Value.ToSize(1)),
                FrameRate = compositor.Scene.GetFrameRate(),
                EvaluationServices = services?.Fork(compositor, this) ?? new EvaluationServices(compositor, this),
                DisabledObjects = compositor.DisabledObjects,
                ColorStagesSkipped = compositor.ColorStagesSkipped,
            };
        }

        public override CompositionEvaluationSession CreateEvaluationSession()
            => new SceneEvaluationSession(_compositor, Target,
                (EvaluationServices)EvaluationServices!);

        public void InheritConditions(CompositionContext? conditions)
        {
            if (conditions is null) return;
            // A nested scene keeps its own frame domain and query services.
            Conditions = conditions.Conditions with { TargetDomain = TargetDomain, EvaluationServices = EvaluationServices };
            DisableResourceShare |= conditions.DisableResourceShare;
            InputBoundaries = conditions.InputBoundaries;
        }

        public IList<Element> CurrentElements { get; set; }

        // A captured context outlives this frame's pooled element list, which is released after evaluation.
        public override CompositionContext WithTime(MediaTime time)
        {
            var result = (CompositorContext)base.WithTime(time);
            result.CurrentElements = [];
            result.FrameBeneath = null;
            return result;
        }

        public CompositionTarget Target { get; set; }

        // The frame's own time while a transition evaluates one of its sides at a held time. Elements a
        // portal draws into that side that the frame's time still covers are not the side's own content and
        // have not ended, so they play on at the frame's time, and both sides share one evaluation of them.
        // Elements the frame does not draw but the side's time covers, such as the next shot before the
        // cut, hold at that time like the side's own content.
        public MediaTime? FrameTime { get; set; }

        // The time of the transition side being collected, while FrameTime is set. An input that plays at
        // the frame's time changes Time, so the inputs its own portals draw read the side's time from here.
        public MediaTime? SideTime { get; set; }

        // What each top-level element of the frame added, while the frame's main loop collects it.
        public FrameBeneath? FrameBeneath { get; set; }

        public GraphicsFrameState? GraphicsState { get; set; }

        public bool IsMatteSource { get; init; }

        public TrackMatteContent ResolveMatte(Element element)
        {
            TrackMatteContent source = element.GetMatteSourceZIndex(_compositor.Scene, GraphicsState!.Time) is { } z
                ? _compositor.GetMatteSource(z, this) : _compositor._emptyMatteSource;
            ContainsScene3D |= GraphicsState.ContainsScene3D;
            return source;
        }

        private static ProxyPreset ToPreset(int value)
        {
            return Enum.IsDefined(typeof(ProxyPreset), value)
                ? (ProxyPreset)value
                : ProxyPreset.Quarter;
        }

        public void EvaluateElementIntoFlow(Element element)
        {
            using var tmpObjects = new PooledList<EngineObject>();
            MediaTime time = Time;
            Time = GetPortalInputTime(element, time, FrameTime, SideTime);

            try
            {
                _compositor.CollectResourcesFromElement(element, this, tmpObjects);
            }
            finally
            {
                Time = time;
            }
        }
    }

    public CompositionFrame EvaluateGraphics(MediaTime time) => EvaluateGraphics(time, null, null);

    /// <summary>Evaluates a frame with the output's actual rate and cancellation policy.</summary>
    public CompositionFrame EvaluateGraphics(MediaTime time, Rational? frameRate, CancellationToken cancellationToken = default)
        => EvaluateGraphics(time, null, null, null, cancellationToken, frameRate);

    /// <summary>Evaluates a nested scene while inheriting its caller's evaluation conditions.</summary>
    internal CompositionFrame EvaluateGraphicsWithContext(MediaTime time, CompositionContext conditions)
        => EvaluateGraphics(time, null, null, conditions);

    internal CompositionFrame EvaluateGraphicsWithBypassedFilterEffect(MediaTime time, FilterEffect effect)
        => EvaluateGraphics(time, null, null, bypassedFilterEffect: effect);

    // Inherited conditions bring their own cancellation and frame rate; cancellationToken and frameRate apply
    // only without them.
    private CompositionFrame EvaluateGraphics(MediaTime time, int? layer, EvaluationServices? services,
        CompositionContext? conditions = null, CancellationToken cancellationToken = default, Rational? frameRate = null,
        FilterEffect? bypassedFilterEffect = null)
    {
        CancellationToken token = conditions?.CancellationToken ?? cancellationToken;
        token.ThrowIfCancellationRequested();
        // A layer query evaluates the requester's frame, not one of this compositor's own.
        if (services is null) BeginQueryGeneration();
        DisposeDetachedResources();
        using var currentElements = new PooledList<Element>();
        SortLayers(time, currentElements, CompositionTarget.Graphics);

        using var tmpObjects = new PooledList<EngineObject>();
        using var flow = new PooledList<EngineObject.Resource>();
        using var allResources = new PooledList<EngineObject.Resource>();
        var ctx = new CompositorContext(time, this, flow, currentElements, CompositionTarget.Graphics);
        ctx.InheritConditions(conditions);
        if (bypassedFilterEffect is not null)
            ctx.Conditions = ctx.Conditions with { BypassedFilterEffect = bypassedFilterEffect };
        if (conditions is null)
        {
            ctx.CancellationToken = cancellationToken;
            if (frameRate.HasValue) ctx.FrameRate = frameRate.Value;
        }
        if (services is not null)
            ctx.EvaluationServices = services;

        // A scene change between two backdrops takes the layers beneath it out of the whole frame, so only the
        // frame's own evaluation, not a query for one layer, records what each element added.
        FrameBeneath? beneath = layer is null && Scene.HasTransitions ? new FrameBeneath(allResources, flow) : null;
        ctx.FrameBeneath = beneath;
        if (beneath is not null) ctx.GraphicsState!.Evaluated = [];

        // 途中でcurrentElementsが変わる可能性があるのでforループで回す
        for (int index = 0; index < currentElements.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            Element element = currentElements[index];
            if (layer.HasValue && element.ZIndex.Value != layer.Value) continue;
            flow.Clear();
            CollectResourcesFromElement(element, ctx, tmpObjects);

            beneath?.Ranges.Add(new FrameRange(element, allResources.Count, flow.Count));
            allResources.AddRange(flow.Span);
        }

        var frame = new CompositionFrame(
            [.. allResources],
            new(time, new MediaTime(1)),
            Scene.FrameSize.Value,
            null)
        {
            ContainsScene3D = ctx.ContainsScene3D || ctx.GraphicsState?.ContainsScene3D == true,
            CancellationToken = token,
        };
        if (services is null)
            ((EvaluationServices)ctx.EvaluationServices!).Frame = frame;
        return frame;
    }

    internal int AudioEligibilityVersion => Volatile.Read(ref _audioEligibilityVersion);

    public CompositionFrame EvaluateAudio(TimeRange timeRange) => EvaluateAudio(timeRange, null);

    internal CompositionFrame EvaluateAudio(TimeRange timeRange, CompositionContext? conditions)
    {
        conditions?.CancellationToken.ThrowIfCancellationRequested();
        BeginQueryGeneration();
        DisposeDetachedResources();
        AudioEligibilitySnapshot eligibility = GetAudioEligibility();

        using var currentElements = new PooledList<Element>();
        SortLayers(timeRange, currentElements, CompositionTarget.Audio);

        using var activeObjects = new PooledList<EngineObject>();
        foreach (Element element in currentElements)
        {
            if (eligibility.ByElement.TryGetValue(element, out EngineObject[]? objects))
                activeObjects.AddRange(objects);
        }

        using var eligibleObjects = new PooledList<EngineObject>();

        using var tmpObjects = new PooledList<EngineObject>();
        using var flow = new PooledList<EngineObject.Resource>();
        using var allResources = new PooledList<EngineObject.Resource>();
        var ctx = new CompositorContext(timeRange.Start, this, flow, currentElements, CompositionTarget.Audio,
            conditions?.EvaluationServices as EvaluationServices);
        ctx.InheritConditions(conditions);

        // 途中でcurrentElementsが変わる可能性があるのでforループで回す
        for (int index = 0; index < currentElements.Count; index++)
        {
            flow.Clear();
            CollectResourcesFromElement(currentElements[index], ctx, tmpObjects);

            allResources.AddRange(flow.Span);
            foreach (EngineObject.Resource resource in flow.Span)
            {
                eligibleObjects.Add(resource.RequireOriginal());
            }
        }

        return new CompositionFrame(
            [.. allResources],
            timeRange,
            Scene.FrameSize.Value,
            eligibility.Objects.WithChanges(activeObjects, eligibleObjects))
        {
            AudioLayerId = GetLayerSnapshot().GetLayerId
        };
    }

    // Analyze a selected element through the same flow as playback, including its SoundGroups and
    // portal inputs. Other tracks are available to portals but are not mixed as independent roots.
    internal CompositionFrame EvaluateElementAudio(Element element, TimeRange timeRange)
    {
        DisposeDetachedResources();
        using var currentElements = new PooledList<Element>();
        SortLayers(timeRange, currentElements, CompositionTarget.Audio);
        using var objects = new PooledList<EngineObject>();
        using var flow = new PooledList<EngineObject.Resource>();
        var context = new CompositorContext(timeRange.Start, this, flow, currentElements, CompositionTarget.Audio);
        CollectResourcesFromElement(element, context, objects);
        return new CompositionFrame([.. flow], timeRange, Scene.FrameSize.Value,
            new CompositionEligibility(flow.Select(resource => resource.RequireOriginal())));
    }

    private AudioEligibilitySnapshot GetAudioEligibility()
    {
        int version = AudioEligibilityVersion;
        if (_audioEligibility is { } cached && cached.Version == version) return cached;

        SceneLayerSnapshot layers = GetLayerSnapshot();
        var byElement = new Dictionary<Element, EngineObject[]>(ReferenceEqualityComparer.Instance);
        using var objects = new PooledList<EngineObject>();
        using var allObjects = new PooledList<EngineObject>();
        foreach (Element element in Scene.Children)
        {
            if (!element.IsEnabled.Value || layers.ShouldSkip(element.ZIndex.Value, CompositionTarget.Audio)) continue;
            objects.Clear();
            element.CollectObjects(CompositionTarget.Audio, objects);
            if (objects.Count == 0) continue;
            byElement.Add(element, objects.ToArray());
            allObjects.AddRange(objects.Span);
        }
        return _audioEligibility = new AudioEligibilitySnapshot(version, new CompositionEligibility(allObjects), byElement);
    }

    private sealed record AudioEligibilitySnapshot(int Version, CompositionEligibility Objects,
        Dictionary<Element, EngineObject[]> ByElement);

    private void CollectResourcesFromElement(
        Element element, CompositorContext context, PooledList<EngineObject> tmpObjects)
    {
        if (context.Target == CompositionTarget.Graphics
            && GetTransitionParticipants().Contains(element)
            && ElementTransitions.TryGetActive(element, context.Time, out TransitionBoundary boundary))
        {
            if (context.IsMatteSource || boundary.HasVisibleSide)
            {
                CollectTransition(boundary, context);
            }
            return;
        }

        CollectElementObjects(element, context, tmpObjects);
    }

    // Evaluates both elements of an active boundary transition, each at its own time, and hands them to
    // the transition's presenter, which takes the place of both elements' drawables in the flow.
    private void CollectTransition(in TransitionBoundary boundary, CompositorContext context)
    {
        using var from = new PooledList<EngineObject.Resource>();
        using var to = new PooledList<EngineObject.Resource>();
        using var tmpObjects = new PooledList<EngineObject>();
        IList<EngineObject.Resource>? oldFlow = context.Flow;
        MediaTime time = context.Time;
        IList<Element> elements = context.CurrentElements;
        // A portal takes the elements it draws out of the ones it is given, so each side starts from the
        // frame's elements; whatever either side took stays out of the frame afterwards.
        Element[] inputs = [.. elements];
        MediaTime? frameTime = context.FrameTime;
        context.FrameTime = time;
        try
        {
            if (boundary.Outgoing is { } outgoing)
            {
                CollectTransitionSide(outgoing, boundary.GetOutgoingTime(time), time, inputs, elements, from, context, tmpObjects);
            }

            if (boundary.Incoming is { } incoming)
            {
                CollectTransitionSide(incoming, boundary.GetIncomingTime(time), time, inputs, elements, to, context, tmpObjects);
            }
        }
        finally
        {
            context.FrameTime = frameTime;
            context.CurrentElements = elements;
            context.Flow = oldFlow;
            context.Time = time;
        }

        var transitionResource = (ClipTransition.Resource)GetOrCreateResource(boundary.Transition, context);
        ClipTransitionPresenter presenter = _transitionPresenters.GetValue(
            boundary.Transition,
            static _ => new ClipTransitionPresenter());
        int zIndex = (boundary.Incoming ?? boundary.Outgoing)!.ZIndex.Value;
        if (presenter.ZIndex.Value != zIndex)
        {
            presenter.ZIndex.Value = zIndex;
        }

        if (context.FrameBeneath is { } beneath && ReferenceEquals(oldFlow, beneath.TopFlow)
            && ElementTransitions.IsSceneChange(boundary))
        {
            TakeBeneath(boundary, zIndex, beneath, from, to, context, tmpObjects);
        }

        var presenterResource = (ClipTransitionPresenter.Resource)GetOrCreateResource(presenter, context);
        float progress = ClipTransition.Ease(transitionResource.Easing, boundary.GetLinearProgress(time));
        presenterResource.SetInputs(transitionResource, from.Span, to.Span, progress);
        oldFlow?.Add(presenterResource);
    }

    // A scene change between two backdrops: each side takes the frame's layers beneath the boundary's layer as
    // they are at its own time, beneath its own drawables, and those layers leave the frame, so they are drawn
    // only through the transition. A side at the frame's time takes them as the frame evaluated them. A held
    // side shares those its time also covers, and evaluates at its time those only its time covers.
    private void TakeBeneath(
        in TransitionBoundary boundary, int layer, FrameBeneath beneath, PooledList<EngineObject.Resource> from,
        PooledList<EngineObject.Resource> to, CompositorContext context, PooledList<EngineObject> tmpObjects)
    {
        MediaTime frameTime = context.Time;
        // The frame draws its layers in order, so those beneath the boundary's layer lead it.
        int ranges = 0;
        while (ranges < beneath.Ranges.Count && beneath.Ranges[ranges].Element.ZIndex.Value < layer) ranges++;
        int count = ranges == 0 ? 0 : beneath.Ranges[ranges - 1].End;

        using (PooledList<EngineObject.Resource> outgoing = CollectBeneath(
                   boundary.GetOutgoingTime(frameTime), layer, beneath, ranges, count, context, tmpObjects))
        {
            from.InsertRange(0, outgoing.Span);
        }

        using (PooledList<EngineObject.Resource> incoming = CollectBeneath(
                   boundary.GetIncomingTime(frameTime), layer, beneath, ranges, count, context, tmpObjects))
        {
            to.InsertRange(0, incoming.Span);
        }

        beneath.Resources.RemoveRange(0, count);
        beneath.Ranges.RemoveRange(0, ranges);
        for (int i = 0; i < beneath.Ranges.Count; i++)
        {
            FrameRange range = beneath.Ranges[i];
            beneath.Ranges[i] = range with { Start = range.Start - count };
        }
    }

    // The layers beneath one side of a scene change at sideTime, in drawing order.
    private PooledList<EngineObject.Resource> CollectBeneath(
        MediaTime sideTime, int layer, FrameBeneath beneath, int ranges, int count, CompositorContext context,
        PooledList<EngineObject> tmpObjects)
    {
        MediaTime frameTime = context.Time;
        var result = new PooledList<EngineObject.Resource>(count);
        if (sideTime == frameTime)
        {
            result.AddRange(beneath.Resources.Span[..count]);
            return result;
        }

        // What the frame drew that this time also covers is shared as the frame evaluated it, along with the
        // layers a shared portal draws, which the held side leaves inside it.
        HashSet<Element> participants = GetTransitionParticipants();
        var entries = new Dictionary<Element, (int Start, int Count)>(ReferenceEqualityComparer.Instance);
        var portalled = new List<(int Start, int End)>();
        for (int i = 0; i < ranges; i++)
        {
            FrameRange range = beneath.Ranges[i];
            if (!IsShared(range.Element, sideTime, frameTime, participants)) continue;

            entries[range.Element] = (range.Start, range.Count);
            AddPortalSpans(range.Element, frameTime, context, portalled);
        }

        // A portal element a shared portal draws, such as a group inside a group, draws its own layers inside
        // it too. Each span lies above the element whose portal it is, so following them ends.
        if (portalled.Count > 0)
        {
            using var atFrameTime = new PooledList<Element>();
            SortLayers(frameTime, atFrameTime, CompositionTarget.Graphics);
            var followed = new HashSet<Element>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < portalled.Count; i++)
            {
                (int start, int end) = portalled[i];
                foreach (Element element in atFrameTime)
                {
                    int z = element.ZIndex.Value;
                    if (z >= start && z <= end && followed.Add(element))
                    {
                        AddPortalSpans(element, frameTime, context, portalled);
                    }
                }
            }
        }

        // What only this time covers is evaluated at it, through its own list, so its portals take from what
        // this side holds and the frame's list stays as the main loop left it.
        using var held = new PooledList<Element>();
        using var atSideTime = new PooledList<Element>();
        SortLayers(sideTime, atSideTime, CompositionTarget.Graphics);
        foreach (Element element in atSideTime)
        {
            int z = element.ZIndex.Value;
            if (z >= layer || element.CoversGraphicsTime(frameTime)) continue;
            if (RunsTransitionInto(element, sideTime, frameTime, participants)) continue;
            if (context.GraphicsState?.Evaluated?.Contains(element) == true) continue;
            if (portalled.Exists(span => z >= span.Start && z <= span.End)) continue;

            held.Add(element);
        }

        using var heldResources = new PooledList<EngineObject.Resource>();
        var heldEntries = new Dictionary<Element, (int Start, int Count)>(ReferenceEqualityComparer.Instance);
        IList<EngineObject.Resource>? flow = context.Flow;
        IList<Element> elements = context.CurrentElements;
        MediaTime? outerFrameTime = context.FrameTime;
        MediaTime? outerSideTime = context.SideTime;
        using var sideFlow = new PooledList<EngineObject.Resource>();
        try
        {
            context.Time = sideTime;
            context.FrameTime = frameTime;
            context.SideTime = sideTime;
            context.CurrentElements = held;
            for (int i = 0; i < held.Count; i++)
            {
                Element element = held[i];
                sideFlow.Clear();
                context.Flow = sideFlow;
                CollectResourcesFromElement(element, context, tmpObjects);
                heldEntries[element] = (heldResources.Count, sideFlow.Count);
                heldResources.AddRange(sideFlow.Span);
            }
        }
        finally
        {
            context.Flow = flow;
            context.CurrentElements = elements;
            context.FrameTime = outerFrameTime;
            context.SideTime = outerSideTime;
            context.Time = frameTime;
        }

        using var order = new PooledList<Element>(entries.Count + heldEntries.Count);
        order.AddRange(entries.Keys);
        order.AddRange(heldEntries.Keys);
        Scene.ElementIndex.Sort(order);
        foreach (Element element in order)
        {
            if (entries.TryGetValue(element, out (int Start, int Count) shared))
            {
                result.AddRange(beneath.Resources.Span.Slice(shared.Start, shared.Count));
            }
            else
            {
                (int start, int length) = heldEntries[element];
                result.AddRange(heldResources.Span.Slice(start, length));
            }
        }

        return result;
    }

    // The layers element's enabled portals draw at time, as PortalObject takes them. An element hidden from the
    // output is not evaluated, so its portals take nothing.
    private static void AddPortalSpans(Element element, MediaTime time, CompositorContext context, List<(int Start, int End)> spans)
    {
        if (!element.IsVisibleInOutput.Value) return;

        MediaTime previous = context.Time;
        context.Time = time;
        try
        {
            foreach (EngineObject obj in element.Objects)
            {
                if (obj is PortalObject { IsEnabled.Value: true } portal)
                {
                    spans.Add(portal.GetInputLayerSpan(context.Get(portal.Count)));
                }
            }
        }
        finally
        {
            context.Time = previous;
        }
    }

    // What one top-level element added to the frame: Count resources from Start in the frame's list.
    private readonly record struct FrameRange(Element Element, int Start, int Count)
    {
        public int End => Start + Count;
    }

    // The frame's resources and what each top-level element added to them, while the main loop collects them.
    private sealed class FrameBeneath(PooledList<EngineObject.Resource> resources, IList<EngineObject.Resource> topFlow)
    {
        public PooledList<EngineObject.Resource> Resources { get; } = resources;

        // The list the main loop collects each element into, which tells a transition collected at the top.
        public IList<EngineObject.Resource> TopFlow { get; } = topFlow;

        public List<FrameRange> Ranges { get; } = [];
    }

    // Evaluates one side of a transition at its own time into flow. Its portals reach what the frame shares
    // with this time, played at the frame's time: the frame's elements this time also covers, and the
    // frame's transitions still running at this time. They also reach what the frame does not draw but this
    // time covers, held at this time, such as the next shot before the cut or the previous shot past it.
    // Each side so draws its own shot, and nothing is evaluated at two times in one frame, where both
    // results would share one resource. A side whose portals reach a shared flow operator keeps the frame's
    // elements instead, as before. The frame's elements the side took are removed from elements.
    private void CollectTransitionSide(
        Element side, MediaTime sideTime, MediaTime frameTime, Element[] inputs, IList<Element> elements,
        PooledList<EngineObject.Resource> flow, CompositorContext context, PooledList<EngineObject> tmpObjects)
    {
        using var sideInputs = new PooledList<Element>(inputs.Length);
        HashSet<Element> participants = GetTransitionParticipants();
        if (sideTime == frameTime || !HasPortal(side)
            || ReachesSharedFlowOperator(side, sideTime, frameTime, inputs, participants, context))
        {
            sideInputs.AddRange(inputs);
        }
        else
        {
            using var atSideTime = new PooledList<Element>();
            SortLayers(sideTime, atSideTime, CompositionTarget.Graphics, ignoreSolo: context.IsMatteSource);
            CollectHeldSideInputs(Scene.ElementIndex, sideTime, frameTime, inputs, atSideTime, participants, sideInputs);
        }

        using var offered = new PooledList<Element>(sideInputs.Count);
        offered.AddRange(sideInputs.Span);
        context.Flow = flow;
        context.Time = sideTime;
        context.CurrentElements = sideInputs;
        MediaTime? outerSideTime = context.SideTime;
        context.SideTime = sideTime;
        try
        {
            CollectElementObjects(side, context, tmpObjects);
        }
        finally
        {
            context.CurrentElements = elements;
            context.SideTime = outerSideTime;
        }

        RemoveConsumedSideInputs(elements, sideInputs, offered.Span);
    }

    internal static MediaTime GetPortalInputTime(Element element, MediaTime time, MediaTime? frameTime, MediaTime? sideTime)
        => frameTime is { } frame ? element.CoversGraphicsTime(frame) ? frame : sideTime ?? time : time;

    internal static void CollectHeldSideInputs(SceneElementIndex index, MediaTime sideTime, MediaTime frameTime,
        ReadOnlySpan<Element> inputs, PooledList<Element> atSideTime, HashSet<Element> participants, PooledList<Element> sideInputs)
    {
        foreach (Element input in inputs)
        {
            if (IsShared(input, sideTime, frameTime, participants)) sideInputs.Add(input);
        }

        foreach (Element element in atSideTime)
        {
            // A transition the frame runs into this time reaches the side through its frame element.
            if (!element.CoversGraphicsTime(frameTime)
                && !RunsTransitionInto(element, sideTime, frameTime, participants))
            {
                sideInputs.Add(element);
            }
        }

        index.Sort(sideInputs);
    }

    internal static void RemoveConsumedSideInputs(IList<Element> elements, PooledList<Element> sideInputs,
        ReadOnlySpan<Element> offered)
    {
        if (sideInputs.Count == offered.Length) return;

        var remaining = new HashSet<Element>(sideInputs, ReferenceEqualityComparer.Instance);
        foreach (Element element in offered)
        {
            if (!remaining.Contains(element)) elements.Remove(element);
        }
    }

    // Whether the side's portals at sideTime reach an input the side shares with the frame that feeds a flow
    // operator, such as a group, through a portal of its own. Both sides evaluate that input into one
    // shared resource, so its portal must see the same inputs on both, and the side keeps the frame's.
    private static bool ReachesSharedFlowOperator(
        Element side, MediaTime sideTime, MediaTime frameTime, Element[] inputs, HashSet<Element> participants,
        CompositorContext context)
    {
        MediaTime time = context.Time;
        context.Time = sideTime;
        try
        {
            foreach (EngineObject obj in side.Objects)
            {
                if (obj is not PortalObject { IsEnabled.Value: true } portal) continue;

                if (ReachesSharedFlowOperator(portal.GetInputLayerSpan(context.Get(portal.Count)), inputs,
                        sideTime, frameTime, participants)) return true;
            }

            return false;
        }
        finally
        {
            context.Time = time;
        }
    }

    internal static bool ReachesSharedFlowOperator((int First, int Last) span, ReadOnlySpan<Element> inputs,
        MediaTime sideTime, MediaTime frameTime, HashSet<Element> participants)
    {
        foreach (Element input in inputs)
        {
            if (input.ZIndex.Value >= span.First && input.ZIndex.Value <= span.Last
                && HasPortal(input) && FeedsGraphicsFlowOperator(input)
                && IsShared(input, sideTime, frameTime, participants))
            {
                return true;
            }
        }

        return false;
    }

    // Whether a side at sideTime shares the frame's input with the frame, which plays it at the frame's
    // time: sideTime covers it too, or a transition it runs at the frame's time reaches sideTime.
    private static bool IsShared(Element input, MediaTime sideTime, MediaTime frameTime, HashSet<Element> participants)
    {
        return input.CoversGraphicsTime(sideTime) || RunsTransitionInto(input, sideTime, frameTime, participants);
    }

    // Whether a transition that element runs at frameTime reaches sideTime.
    private static bool RunsTransitionInto(Element element, MediaTime sideTime, MediaTime frameTime, HashSet<Element> participants)
    {
        return participants.Contains(element)
               && ElementTransitions.TryGetActive(element, frameTime, out TransitionBoundary running)
               && running.Region.Contains(sideTime);
    }

    // Whether element draws through a flow operator in the graphics pass, selected as Element.CollectObjects
    // selects it.
    private static bool FeedsGraphicsFlowOperator(Element element)
    {
        foreach (EngineObject obj in element.Objects)
        {
            if (obj is IFlowOperator && obj.IsEnabled.Value
                && obj.GetCompositionTarget() is CompositionTarget.Unknown or CompositionTarget.Graphics)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool HasPortal(Element element)
    {
        foreach (EngineObject obj in element.Objects)
        {
            if (obj is PortalObject { IsEnabled.Value: true }) return true;
        }

        return false;
    }

    private void CollectElementObjects(
        Element element, CompositorContext context, PooledList<EngineObject> tmpObjects)
    {
        // Check at the element, including transition sides and portal inputs. A matte evaluates the
        // same layer independently of its output visibility.
        if (context.Target == CompositionTarget.Graphics && !context.IsMatteSource
            && !element.IsVisibleInOutput.Value) return;
        // Cyclic matte references have no source contribution at the repeated element.
        if (context.GraphicsState is { } state && !state.Evaluating.Add(element)) return;
        context.GraphicsState?.Evaluated?.Add(element);
        using var flow = new PooledList<EngineObject.Resource>();
        var oldFlow = context.Flow;
        context.Flow = flow;
        try
        {
            tmpObjects.Clear();
            element.CollectObjects(context.Target, tmpObjects);
            foreach (EngineObject obj in tmpObjects.Span)
            {
                flow.Add(GetOrCreateResource(obj, context));
            }

            if (context.GraphicsState is { } frame && element.HasTrackMatte)
            {
                ApplyTrackMatte(element, element.GetMatteSourceZIndex(Scene, frame.Time), context, flow);
            }

            foreach (EngineObject.Resource resource in flow)
            {
                oldFlow?.Add(resource);
            }
        }
        finally
        {
            context.Flow = oldFlow;
            context.GraphicsState?.Evaluating.Remove(element);
        }
    }

    private sealed class ResourceEntry(EngineObject.Resource resource, EventHandler<HierarchyAttachmentEventArgs> detached)
    {
        public EngineObject.Resource Resource { get; set; } = resource;
        public EventHandler<HierarchyAttachmentEventArgs> Detached { get; } = detached;
    }

    private EngineObject.Resource GetOrCreateResource(EngineObject obj, CompositionContext context)
    {
        EngineObject? previousOwner = context.FlowOwner;
        context.FlowOwner = obj;
        try
        {
            EngineObject.Resource resource = GetOrCreateResourceCore(obj, context);
            _usedSampleResources?.Add(resource);
            return resource;
        }
        finally
        {
            context.FlowOwner = previousOwner;
        }
    }

    private EngineObject.Resource GetOrCreateResourceCore(EngineObject obj, CompositionContext context)
    {
        bool matte = context is CompositorContext { IsMatteSource: true };
        var cache = matte ? _matteResourceCache : _resourceCache;
        if (!cache.TryGetValue(obj, out var entry))
        {
            var resource = obj.ToResource(context);
            entry = new ResourceEntry(resource, AddDetachedHandler(obj, matte));
            cache.Add(obj, entry);
        }
        else if (entry.Resource.IsDisposed)
        {
            entry.Resource = obj.ToResource(context);
        }
        else
        {
            bool _ = false;
            entry.Resource.Reconcile(obj, context, ref _);
        }

        return entry.Resource;
    }

    private EventHandler<HierarchyAttachmentEventArgs> AddDetachedHandler(EngineObject obj, bool matte)
    {
        var weakRef = new WeakReference<SceneCompositor>(this);

        void Handler(object? sender, HierarchyAttachmentEventArgs e)
        {
            if (sender is not EngineObject senderObj) return;

            if (weakRef.TryGetTarget(out SceneCompositor? compositor))
            {
                lock (compositor._resourceCacheLock)
                {
                    var cache = matte ? compositor._matteResourceCache : compositor._resourceCache;
                    if (cache.TryGetValue(senderObj, out var entry))
                    {
                        cache.Remove(senderObj);
                        // Detachment runs on the editing thread while the current frame may still use this
                        // resource's native handles. Transfer ownership before teardown can drain the queue.
                        compositor._detachedResources.Enqueue(entry.Resource);
                    }
                }
            }

            senderObj.DetachedFromHierarchy -= Handler;
        }

        obj.DetachedFromHierarchy += Handler;
        return Handler;
    }

    private void DisposeDetachedResources()
    {
        while (_detachedResources.TryDequeue(out var resource))
        {
            resource.Dispose();
        }
    }

    private void ReleaseCachedResources(HashSet<EngineObject.Resource>? retained = null)
    {
        lock (_resourceCacheLock)
        {
            foreach (var cache in new[] { _resourceCache, _matteResourceCache })
            {
                foreach (var pair in cache)
                {
                    if (retained?.Contains(pair.Value.Resource) == true) continue;
                    pair.Key.DetachedFromHierarchy -= pair.Value.Detached;
                    cache.Remove(pair.Key);
                    _detachedResources.Enqueue(pair.Value.Resource);
                }
            }
        }

        // Release outside the lock because disposal can itself trigger hierarchy callbacks.
        DisposeDetachedResources();
    }

    // timeに掛かるElementを、solo/muteでフィルタしつつZIndex順に振り分ける
    private void SortLayers(MediaTime time, PooledList<Element> currentElements, CompositionTarget target, bool ignoreSolo = false)
        => CollectFrameElements(Scene, time, currentElements, target,
            target == CompositionTarget.Graphics ? GetTransitionParticipants() : null, GetLayerSnapshot(), ignoreSolo);

    internal static void CollectFrameElements(Scene scene, MediaTime time, PooledList<Element> currentElements,
        CompositionTarget target, HashSet<Element>? participants, SceneLayerSnapshot? layers = null, bool ignoreSolo = false)
    {
        bool graphics = target == CompositionTarget.Graphics;
        SceneElementIndex index = scene.ElementIndex;
        using var candidates = new PooledList<Element>();
        MediaTime tolerance = graphics ? ElementTransitions.AdjacencyTolerance : MediaTime.Zero;
        index.Query(time - tolerance, time + tolerance, candidates);
        foreach (Element item in candidates)
        {
            if (!item.IsEnabled.Value || !(graphics ? item.CoversGraphicsTime(time) : item.Range.Contains(time))) continue;
            if (layers?.ShouldSkip(item.ZIndex.Value, target, ignoreSolo) == true) continue;
            currentElements.Add(item);
        }
        index.Sort(currentElements);

        if (participants != null)
        {
            KeepOneElementPerTransition(time, currentElements, participants);
        }
    }

    // Both elements of a boundary are drawn by its transition, so when both fall on this frame (they
    // overlap, or touch within the tolerance) only the first stays in the list to collect it. Past the
    // transition the incoming element has taken over, so an outgoing element that runs on beyond it (the
    // two overlap and the span stops at the incoming element's middle) stays hidden.
    private static void KeepOneElementPerTransition(MediaTime time, PooledList<Element> currentElements, HashSet<Element> participants)
    {
        if (participants.Count == 0) return;

        for (int i = currentElements.Count - 1; i >= 0; i--)
        {
            Element element = currentElements[i];
            if (participants.Contains(element)
                && ElementTransitions.GetBoundaryAtEnd(element) is { Incoming: { } next } end
                && time >= end.Region.End
                && currentElements.Contains(next))
            {
                currentElements.RemoveAt(i);
            }
        }

        for (int i = 0; i < currentElements.Count; i++)
        {
            if (participants.Contains(currentElements[i])
                && ElementTransitions.TryGetActive(currentElements[i], time, out TransitionBoundary boundary)
                && boundary is { Outgoing: { } outgoing, Incoming: { } incoming })
            {
                Element partner = ReferenceEquals(currentElements[i], outgoing) ? incoming : outgoing;
                int index = currentElements.IndexOf(partner);
                if (index > i)
                {
                    currentElements.RemoveAt(index);
                }
            }
        }
    }

    // timeRangeに掛かるElementを、solo/muteでフィルタしつつZIndex順に振り分ける
    private void SortLayers(TimeRange timeRange, PooledList<Element> currentElements, CompositionTarget target)
    {
        SceneLayerSnapshot snapshot = GetLayerSnapshot();
        SceneElementIndex index = Scene.ElementIndex;
        using var candidates = new PooledList<Element>();
        index.Query(timeRange.Start, timeRange.End, candidates);
        foreach (Element item in candidates)
        {
            if (!item.IsEnabled.Value || !item.Range.Intersects(timeRange)) continue;
            if (snapshot.ShouldSkip(item.ZIndex.Value, target)) continue;
            currentElements.Add(item);
        }
        index.Sort(currentElements);
    }

    // Concurrent rebuilds after an invalidation are benign: each produces an
    // equivalent snapshot and the last write wins.
    private SceneLayerSnapshot GetLayerSnapshot() => _layerSnapshot ??= SceneLayerSnapshot.Build(Scene);

    private void OnLayersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _layerSnapshot = null;
        Interlocked.Increment(ref _audioEligibilityVersion);
    }

    private HashSet<Element> GetTransitionParticipants()
    {
        int version = Volatile.Read(ref _participantsVersion);
        if (_participants is { } snapshot && snapshot.Version == version) return snapshot.Elements;

        HashSet<Element> elements = ElementTransitions.GetParticipants(Scene);
        _participants = new ParticipantSnapshot(version, elements);
        return elements;
    }

    private void OnSceneEdited(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _participantsVersion);
        Interlocked.Increment(ref _audioEligibilityVersion);
        _audioEligibility = null;
    }

    private void OnChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnSceneEdited(sender, e);

    private sealed record ParticipantSnapshot(int Version, HashSet<Element> Elements);

    private void OnLayerAttached(TimelineLayer layer)
    {
        ObserveLayer(layer);
        _layerSnapshot = null;
    }

    private void OnLayerDetached(TimelineLayer layer)
    {
        ForgetLayer(layer);
        _layerSnapshot = null;
    }

    private void ObserveLayer(TimelineLayer layer)
    {
        layer.ZIndex.ValueChanged += OnLayerZIndexChanged;
        layer.IsSolo.ValueChanged += OnLayerSoloChanged;
        layer.IsAudioMuted.ValueChanged += OnLayerAudioMutedChanged;
    }

    private void ForgetLayer(TimelineLayer layer)
    {
        layer.ZIndex.ValueChanged -= OnLayerZIndexChanged;
        layer.IsSolo.ValueChanged -= OnLayerSoloChanged;
        layer.IsAudioMuted.ValueChanged -= OnLayerAudioMutedChanged;
    }

    private void OnLayerZIndexChanged(object? sender, PropertyValueChangedEventArgs<int> e)
    {
        _layerSnapshot = null;
        Interlocked.Increment(ref _audioEligibilityVersion);
    }

    private void OnLayerSoloChanged(object? sender, PropertyValueChangedEventArgs<bool> e)
    {
        _layerSnapshot = null;
        Interlocked.Increment(ref _audioEligibilityVersion);
    }

    private void OnLayerAudioMutedChanged(object? sender, PropertyValueChangedEventArgs<bool> e)
        => Interlocked.Increment(ref _audioEligibilityVersion);

    public void Dispose()
    {
        DisposeQueryPools();
        Scene.Layers.CollectionChanged -= OnLayersCollectionChanged;
        Scene.Edited -= OnSceneEdited;
        Scene.Children.CollectionChanged -= OnChildrenCollectionChanged;
        Scene.Layers.Attached -= OnLayerAttached;
        Scene.Layers.Detached -= OnLayerDetached;
        foreach (TimelineLayer layer in Scene.Layers)
        {
            ForgetLayer(layer);
        }

        ReleaseCachedResources();
        _matteSources.Clear();
    }
}
