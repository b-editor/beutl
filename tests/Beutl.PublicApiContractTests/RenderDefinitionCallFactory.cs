using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.PublicApiContractTests;

internal static class RenderDefinitionCallFactory
{
    public static OpaqueRenderCall<TState> Opaque<TState>(
        TState state,
        Action<OpaqueRenderSession, TState> execute,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        IEnumerable<RenderResourceSlot>? resources = null,
        IEnumerable<RenderResourceBinding>? bindings = null)
        where TState : notnull
    {
        return OpaqueRenderDefinition<TState>.Create(
            execute,
            bounds,
            hitTest,
            valueCardinality,
            scale,
            deviceGridSensitivity,
            inputReadbacks,
            resources)
            .Call(state, bindings);
    }

    public static OpaqueRenderCall<Action<OpaqueRenderSession>> Opaque(
        Action<OpaqueRenderSession> execute,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        IEnumerable<RenderResourceSlot>? resources = null,
        IEnumerable<RenderResourceBinding>? bindings = null)
    {
        return Opaque(
            execute,
            static (session, action) => action(session),
            bounds,
            hitTest,
            valueCardinality,
            scale,
            deviceGridSensitivity,
            inputReadbacks,
            resources,
            bindings);
    }

    public static GeometryCall<TState> Geometry<TState>(
        TState state,
        Action<GeometrySession, TState> render,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        bool requiresReadback = false,
        IEnumerable<RenderResourceSlot>? resources = null,
        IEnumerable<RenderResourceBinding>? bindings = null)
        where TState : notnull
    {
        return GeometryDefinition<TState>.Create(
            render,
            bounds,
            hitTest,
            requiresReadback,
            resources)
            .Call(state, bindings);
    }

    public static GeometryCall<Action<GeometrySession>> Geometry(
        Action<GeometrySession> render,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        bool requiresReadback = false,
        IEnumerable<RenderResourceSlot>? resources = null,
        IEnumerable<RenderResourceBinding>? bindings = null)
    {
        return Geometry(
            render,
            static (session, action) => action(session),
            bounds,
            hitTest,
            requiresReadback,
            resources,
            bindings);
    }

    public static TargetScopeCall<TState> TargetScope<TState>(
        TState state,
        Action<TargetScopeSession, TState> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
        RenderDeviceGridMapping deviceGridMapping = RenderDeviceGridMapping.Remapped,
        IEnumerable<RenderResourceSlot>? resources = null,
        IEnumerable<RenderResourceBinding>? bindings = null)
        where TState : notnull
    {
        return TargetScopeDefinition<TState>.Create(
            execute,
            bounds,
            hitTest,
            scale,
            deviceGridSensitivity,
            deviceGridMapping,
            resources)
            .Call(state, bindings);
    }

    public static TargetScopeCall<Action<TargetScopeSession>> TargetScope(
        Action<TargetScopeSession> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
        RenderDeviceGridMapping deviceGridMapping = RenderDeviceGridMapping.Remapped,
        IEnumerable<RenderResourceSlot>? resources = null,
        IEnumerable<RenderResourceBinding>? bindings = null)
    {
        return TargetScope(
            execute,
            static (session, action) => action(session),
            bounds,
            hitTest,
            scale,
            deviceGridSensitivity,
            deviceGridMapping,
            resources,
            bindings);
    }

    public static TargetCommandCall<TState> TargetCommand<TState>(
        TState state,
        Action<TargetCommandSession, TState> execute,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access = TargetAccess.ReadWrite,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        IEnumerable<RenderResourceSlot>? resources = null,
        IEnumerable<RenderResourceBinding>? bindings = null)
        where TState : notnull
    {
        return TargetCommandDefinition<TState>.Create(
            execute,
            affectedRegion,
            queryBounds,
            hitTest,
            access,
            inputReadbacks,
            resources)
            .Call(state, bindings);
    }

    public static TargetCommandCall<Action<TargetCommandSession>> TargetCommand(
        Action<TargetCommandSession> execute,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access = TargetAccess.ReadWrite,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        IEnumerable<RenderResourceSlot>? resources = null,
        IEnumerable<RenderResourceBinding>? bindings = null)
    {
        return TargetCommand(
            execute,
            static (session, action) => action(session),
            affectedRegion,
            queryBounds,
            hitTest,
            access,
            inputReadbacks,
            resources,
            bindings);
    }
}
