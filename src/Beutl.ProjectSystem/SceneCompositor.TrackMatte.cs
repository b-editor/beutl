using System.Runtime.CompilerServices;
using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed partial class SceneCompositor
{
    private readonly ConditionalWeakTable<Element, TrackMattePresenter> _mattePresenters = new();
    // Resources are mutable evaluated snapshots. A source includes hidden clips and ignores solo,
    // so reconciling it must not overwrite a transition or portal already collected for output.
    private readonly ConditionalWeakTable<EngineObject, ResourceEntry> _matteResourceCache = new();
    private readonly Dictionary<int, TrackMatteContent> _matteSources = [];

    private sealed class GraphicsFrameState(MediaTime time)
    {
        public MediaTime Time { get; } = time;
        public bool ContainsScene3D { get; set; }
        public Dictionary<int, TrackMatteContent> Sources { get; } = [];
        public HashSet<int> ResolvingSources { get; } = [];
        public HashSet<Element> Evaluating { get; } = [];
    }

    private void ApplyTrackMatte(
        Element element, int sourceZIndex, CompositorContext context, PooledList<EngineObject.Resource> flow)
    {
        TrackMatteContent source = GetMatteSource(sourceZIndex, context.GraphicsState!);
        TrackMattePresenter presenter = _mattePresenters.GetValue(element, static _ => new());
        presenter.ZIndex.Value = element.ZIndex.Value;
        var resource = (TrackMattePresenter.Resource)GetOrCreateResource(presenter, context);
        resource.SetInputs(flow.Span, source, element.MatteMode.Value);
        flow.Clear();
        flow.Add(resource);
    }

    private TrackMatteContent GetMatteSource(int zIndex, GraphicsFrameState state)
    {
        if (state.Sources.TryGetValue(zIndex, out TrackMatteContent? source)) return source;
        if (!state.ResolvingSources.Add(zIndex)) return new TrackMatteContent();

        try
        {
            // Sources run at the frame's time even when a transition holds its target's edge frame.
            // Give portals their own input list so collecting a matte cannot consume output elements.
            using var elements = new PooledList<Element>();
            SortLayers(state.Time, elements, CompositionTarget.Graphics, ignoreSolo: true);
            using var objects = new PooledList<EngineObject>();
            using var flow = new PooledList<EngineObject.Resource>();
            var context = new CompositorContext(state.Time, this, flow, elements, CompositionTarget.Graphics)
            {
                GraphicsState = state,
                IsMatteSource = true,
            };
            for (int i = 0; i < elements.Count; i++)
            {
                if (elements[i].ZIndex.Value == zIndex)
                    CollectResourcesFromElement(elements[i], context, objects);
            }

            if (!_matteSources.TryGetValue(zIndex, out source))
            {
                source = new TrackMatteContent();
                _matteSources.Add(zIndex, source);
            }

            state.ContainsScene3D |= context.ContainsScene3D;
            source.Update(flow.Span);
            state.Sources.Add(zIndex, source);
            return source;
        }
        finally
        {
            state.ResolvingSources.Remove(zIndex);
        }
    }
}
