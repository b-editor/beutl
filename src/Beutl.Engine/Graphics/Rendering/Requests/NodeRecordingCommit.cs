using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>What one recording hands to whoever absorbs it.</summary>
/// <remarks>
/// A value, not an object: it is created once per node visit, read once, and never stored, so the heap
/// object it used to be was pure per-frame cost. Every member is already an immutable array, which is what
/// lets the copy stay a handful of references.
/// </remarks>
internal readonly record struct NodeRecordingCommit(
    ImmutableArray<RecordedRenderFragmentEntry> Fragments,
    ImmutableArray<RenderFragmentReference> Publications,
    ImmutableArray<RenderResource> Resources,
    ImmutableArray<RecordedNestedRenderRequest> NestedRequests,
    ImmutableArray<BuiltInBackdropBinding> BuiltInBackdropBindings,
    ImmutableArray<RenderFragmentReference> Dropped);
