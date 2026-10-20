using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// What one recording hands to whoever absorbs it. A value type: created once per node visit and read once.
/// </summary>
internal readonly record struct NodeRecordingCommit(
    ImmutableArray<RecordedRenderFragmentEntry> Fragments,
    ImmutableArray<RenderFragmentReference> Publications,
    ImmutableArray<RenderResource> Resources,
    ImmutableArray<RecordedNestedRenderRequest> NestedRequests,
    ImmutableArray<BuiltInBackdropBinding> BuiltInBackdropBindings,
    ImmutableArray<RenderFragmentReference> Dropped);
