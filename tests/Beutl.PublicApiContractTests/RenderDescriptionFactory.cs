using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// Builds the operation descriptions a fixture records from a plain session action.
/// </summary>
/// <remarks>
/// A description keys its plan by the execution callback's own identity, so a fixture that does not need
/// per-recording values still has to hand one a static callback and carry the work it wants run as state.
/// Every method here is that one binding - the action is the state, and a shared static callback invokes it -
/// which is the whole of what this file adds. A fixture that does supply per-recording values calls
/// <c>Create</c> on the description directly; wrapping that would only reorder its parameters.
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
        IEnumerable<RenderResourceBinding>? resources = null,
        RenderInputDemandContract inputDemand = default,
        IEnumerable<RenderResourceSlot>? slots = null)
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
            inputDemand,
            slots);
    }

    public static GeometryDescription Geometry(
        Action<GeometrySession> render,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        bool requiresReadback = false,
        RenderInputDemandContract inputDemand = default,
        IEnumerable<RenderResourceBinding>? resources = null,
        IEnumerable<RenderResourceSlot>? slots = null)
    {
        return GeometryDescription.Create(
            render,
            static (session, action) => action(session),
            bounds,
            hitTest,
            requiresReadback,
            inputDemand,
            resources,
            slots);
    }

    public static TargetScopeDescription TargetScope(
        Action<TargetScopeSession> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
        RenderDeviceGridMapping deviceGridMapping = RenderDeviceGridMapping.Remapped,
        RenderScopeTransformSpace transformSpace = RenderScopeTransformSpace.AmbientTarget,
        IEnumerable<RenderResourceBinding>? resources = null,
        IEnumerable<RenderResourceSlot>? slots = null)
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
            resources,
            slots);
    }

    public static TargetCommandDescription TargetCommand(
        Action<TargetCommandSession> execute,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access = TargetAccess.ReadWrite,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        IEnumerable<RenderResourceBinding>? resources = null,
        RenderInputDemandContract inputDemand = default,
        IEnumerable<RenderResourceSlot>? slots = null)
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
            inputDemand,
            slots);
    }
}
