using System.Collections.Immutable;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

internal readonly record struct ExecutionIslandBoundary(
    RenderFragmentId? BeforeFragmentId,
    RenderFragmentId? AfterFragmentId,
    ExecutionIslandBoundaryReason Reason,
    ImmutableArray<SkslBackendLimit> BackendLimits);
