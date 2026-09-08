using System.Collections.Immutable;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering.Requests;

internal readonly record struct ExecutionIslandBoundary(
    int? BeforeFragmentIndex,
    int? AfterFragmentIndex,
    ExecutionIslandBoundaryReason Reason,
    ImmutableArray<SkslBackendLimit> BackendLimits);
