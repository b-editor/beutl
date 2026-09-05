using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.PublicApiContractTests;

/// <summary>Builds test descriptions from a session action and a shared static callback.</summary>
/// <remarks>
/// The action is explicit state, so one-shot fixtures keep a stable execution identity. Tests of BESG003 or
/// BESG004 must call the production factory directly so the analyzer sees their callback.
/// </remarks>
internal static class RenderDescriptionFactory
{
    public static OpaqueRenderDescription Opaque(
        Action<OpaqueRenderSession> execute,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        IReadOnlyList<RenderResourceBinding>? resources = null,
        RenderInputDemandContract inputDemand = default)
    {
        return OpaqueRenderDescription.Create(
            execute,
            static (session, action) => action(session),
            bounds,
            hitTest,
            valueCardinality,
            scale,
            deviceGridSensitivity,
            inputReadbacks,
            resources,
            inputDemand);
    }

    public static GeometryDescription Geometry(
        Action<GeometrySession> render,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        bool requiresReadback = false,
        RenderInputDemandContract inputDemand = default,
        IReadOnlyList<RenderResourceBinding>? resources = null)
    {
        return GeometryDescription.Create(
            render,
            static (session, action) => action(session),
            bounds,
            hitTest,
            requiresReadback,
            inputDemand,
            resources);
    }

    public static TargetScopeDescription TargetScope(
        Action<TargetScopeSession> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
        RenderDeviceGridMapping deviceGridMapping = RenderDeviceGridMapping.Remapped,
        RenderScopeTransformSpace transformSpace = RenderScopeTransformSpace.AmbientTarget,
        IReadOnlyList<RenderResourceBinding>? resources = null)
    {
        return TargetScopeDescription.Create(
            execute,
            static (session, action) => action(session),
            bounds,
            hitTest,
            scale,
            deviceGridSensitivity,
            deviceGridMapping,
            transformSpace,
            resources);
    }

    public static TargetCommandDescription TargetCommand(
        Action<TargetCommandSession> execute,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access = TargetAccess.ReadWrite,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        IReadOnlyList<RenderResourceBinding>? resources = null,
        RenderInputDemandContract inputDemand = default)
    {
        return TargetCommandDescription.Create(
            execute,
            static (session, action) => action(session),
            affectedRegion,
            queryBounds,
            hitTest,
            access,
            inputReadbacks,
            resources,
            inputDemand);
    }
}
