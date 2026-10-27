using System.ComponentModel;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed partial class QueryTools
{
    [McpServerTool(Name = "measure_object_bounds")]
    [Description("Measures contributing RenderNode query bounds for Drawable objects in the current scene. Use before positioning text, backing plates, or centered objects; default Drawable TranslateTransform values are offsets from the alignment-resolved position, not top-left coordinates.")]
    public ToolResult<ObjectBoundsMeasurementResponse> MeasureObjectBounds(
        string? objectId = null,
        string? elementId = null,
        double? timeSeconds = null)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            // Walk the live scene on the session dispatcher: this positioning tool runs while the
            // Avalonia editor may be mutating scene.Children / drawables on the UI thread.
            return session.ReadOnSession(() => MeasureObjectBoundsCore(session, objectId, elementId, timeSeconds));
        });
    }

    private ObjectBoundsMeasurementResponse MeasureObjectBoundsCore(
        IEditingSession session, string? objectId, string? elementId, double? timeSeconds)
    {
        Scene scene = RequireSceneRoot(session);
        Guid? objectGuid = ParseOptionalGuid(objectId, nameof(objectId));
        Guid? elementGuid = ParseOptionalGuid(elementId, nameof(elementId));
        MediaTime time = ParseMeasurementTime(timeSeconds);
        bool timeFiltered = timeSeconds.HasValue;

        Element? selectedElement = ResolveSelectedElement(scene, elementGuid, elementId);
        Drawable? selectedDrawable = ResolveSelectedDrawable(scene, objectGuid, objectId);

        Size canvasSize = new(scene.FrameSize.Value.Width, scene.FrameSize.Value.Height);
        // timeSeconds is scene-relative like every other tool, but Element.Range and the engine's
        // composition clock live on the absolute timeline axis (renderers evaluate time + scene.Start).
        MediaTime absoluteTime = time + scene.Start.Value;
        // Auxiliary graph nodes (Measure / Preview) evaluate their own subtree during ToResource, before any
        // render request exists, and a bounds-unknown effect leaves them nothing finite to resolve against.
        // The frame is the domain the measurement itself composes into, so seed it the way SceneCompositor does.
        var context = new CompositionContext(absoluteTime) { TargetDomain = new Rect(default, canvasSize) };
        var measurements = new List<ObjectBoundsMeasurement>();
        foreach (Element element in scene.Children)
        {
            if (ShouldSkipElement(element, selectedElement, timeFiltered, absoluteTime))
            {
                continue;
            }

            foreach (EngineObject obj in element.Objects)
            {
                if (obj is not Drawable drawable)
                {
                    continue;
                }

                if (ShouldSkipDrawable(drawable, selectedDrawable, timeFiltered))
                {
                    continue;
                }

                measurements.Add(MeasureDrawable(element, drawable, canvasSize, context));
            }
        }

        ThrowIfSelectionUnmeasured(scene, selectedElement, selectedDrawable, measurements.Count, objectId, elementId);

        return new ObjectBoundsMeasurementResponse(
            SchemaVersion.Current,
            session.SessionId,
            session.Source.ToString(),
            scene.Id.Value.ToString(),
            scene.FrameSize.Value.Width,
            scene.FrameSize.Value.Height,
            new ObjectBoundsPoint(scene.FrameSize.Value.Width / 2d, scene.FrameSize.Value.Height / 2d),
            time.ToTimeSpan().ToString("c"),
            timeFiltered,
            "Scene pixel coordinates. TransformedBounds are authoritative axis-aligned scene-space RenderNodeMeasurement.QueryBounds from contributing query fragments. LocalBounds are normalized from the query extents for size only and are not Drawable.MeasureCore results.",
            "Default Drawable AlignmentX/AlignmentY is Center. Choose Left/Center/Right and Top/Center/Bottom independently to suit the object, layout, and intended motion, and check for unintended anchor shifts when animating alignment or drawable size. A pure TranslateTransform(x, y) offsets the chosen alignment-resolved position; for a normal centered drawable in a 1920x1080 scene, TranslateTransform(0, 0) centers it at (960, 540). Bounds are measured through DrawableRenderNode and RenderNodeRenderer.Measure().QueryBounds rather than per-type Drawable.Measure/FilterEffect.TransformBounds estimates.",
            measurements);
    }

    private static Element? ResolveSelectedElement(Scene scene, Guid? elementGuid, string? elementId)
    {
        if (elementGuid is not { } elementGuidValue)
        {
            return null;
        }

        Element? selectedElement = scene.Children.FirstOrDefault(item => item.Id.Value == elementGuidValue);
        if (selectedElement is null)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.StaleHandle,
                $"No Element with Id '{elementId}' exists in the current scene.",
                elementId));
        }

        return selectedElement;
    }

    private static Drawable? ResolveSelectedDrawable(Scene scene, Guid? objectGuid, string? objectId)
    {
        if (objectGuid is not { } objectGuidValue)
        {
            return null;
        }

        var entity = scene.FindById(objectGuidValue);
        if (entity is null)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.StaleHandle,
                $"No object with Id '{objectId}' exists in the current scene.",
                objectId));
        }

        if (entity is not Drawable drawable)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"Object '{objectId}' is a {entity.GetType().FullName}, not a Drawable.",
                objectId,
                "Pass a Drawable object Id from read_document_summary or omit objectId to measure all direct Drawable objects."));
        }

        return drawable;
    }

    private static bool ShouldSkipElement(Element element, Element? selectedElement, bool timeFiltered, MediaTime absoluteTime)
    {
        return (selectedElement is not null && element != selectedElement)
               || (timeFiltered && (!element.IsEnabled.Value || !element.Range.Contains(absoluteTime)));
    }

    private static bool ShouldSkipDrawable(Drawable drawable, Drawable? selectedDrawable, bool timeFiltered)
    {
        return (selectedDrawable is not null && drawable != selectedDrawable)
               || (timeFiltered && !drawable.IsEnabled.Value);
    }

    private static void ThrowIfSelectionUnmeasured(
        Scene scene,
        Element? selectedElement,
        Drawable? selectedDrawable,
        int measurementCount,
        string? objectId,
        string? elementId)
    {
        if (selectedElement is not null && selectedDrawable is not null && measurementCount == 0)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"Drawable '{objectId}' is not a direct object of Element '{elementId}' at the requested time.",
                objectId,
                "Measure the object without elementId, or use the Element that directly contains the object."));
        }

        // A nested (flow/group) drawable exists in the scene but is never a direct element object, so
        // the caller's loop measures nothing. Key the unsupported-nesting error on that fact rather than on
        // timeFiltered — otherwise a time-filtered request silently returns an empty, successful result.
        bool selectedDrawableIsDirectObject = selectedDrawable is not null
            && scene.Children.Any(element => element.Objects.Contains(selectedDrawable));
        if (selectedDrawable is not null && measurementCount == 0 && !selectedDrawableIsDirectObject)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"Drawable '{objectId}' is not a direct object of any Element in the current scene.",
                objectId,
                "measure_object_bounds currently measures direct Drawable objects in timeline Elements. Nested flow/group drawables are reported as an unsupported improvement area."));
        }
    }

    private static Guid? ParseOptionalGuid(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Guid.TryParse(value, out Guid id))
        {
            return id;
        }

        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            $"{parameterName} must be a GUID.",
            value));
    }

    private static MediaTime ParseMeasurementTime(double? timeSeconds)
    {
        if (timeSeconds is not { } seconds)
        {
            return MediaTime.Zero;
        }

        if (!double.IsFinite(seconds) || seconds < 0)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                "timeSeconds must be a finite non-negative number.",
                seconds.ToString("R")));
        }

        return MediaTime.FromSeconds(seconds);
    }

    private static ObjectBoundsMeasurement MeasureDrawable(
        Element element,
        Drawable drawable,
        Size canvasSize,
        CompositionContext context)
    {
        RenderNodeBounds renderNodeBounds = MeasureDrawableRenderNodeBounds(drawable, canvasSize, context);
        Rect transformedBounds = renderNodeBounds.Bounds;
        Rect localBounds = NormalizeBoundsSize(transformedBounds);
        AlignmentX alignmentX = context.Get(drawable.AlignmentX);
        AlignmentY alignmentY = context.Get(drawable.AlignmentY);
        Transform? transform = context.Get(drawable.Transform);
        Matrix userTransform = transform?.CreateMatrix(context) ?? Matrix.Identity;
        ObjectBoundsPoint? userTranslate = userTransform.TryDecomposeTransform(out Vector translate, out _, out _, out _)
            ? new ObjectBoundsPoint(translate.X, translate.Y)
            : null;
        string? note = renderNodeBounds.Note is null
            ? "Measured through DrawableRenderNode and RenderNodeRenderer.Measure().QueryBounds from contributing query fragments. LocalBounds is normalized from query extents for size only."
            : $"{renderNodeBounds.Note} LocalBounds is normalized from query extents for size only.";

        ObjectBoundsPoint? geometryBoundsOrigin = null;
        if (drawable is Shape shapeDrawable)
        {
            using var shapeResource = (Shape.Resource)shapeDrawable.ToResource(context);
            if (shapeResource.GetGeometry() is { } geometryResource)
            {
                Rect geometryBounds = geometryResource.Bounds;
                geometryBoundsOrigin = new ObjectBoundsPoint(geometryBounds.X, geometryBounds.Y);
                if (Math.Abs(geometryBounds.X) > 0.5f || Math.Abs(geometryBounds.Y) > 0.5f)
                {
                    note += $" Geometry path bounds start at ({geometryBounds.X:0.##}, {geometryBounds.Y:0.##}) instead of (0, 0), so the drawn path is offset from the alignment-resolved box by exactly that amount. Author path coordinates with the artwork's top-left at (0, 0), or add TranslateTransform({-geometryBounds.X:0.##}, {-geometryBounds.Y:0.##}).";
                }
            }
        }

        return new ObjectBoundsMeasurement(
            element.Id.Value.ToString(),
            element.Name.Value,
            element.Start.Value.ToTimeSpan().ToString("c"),
            element.Length.Value.ToTimeSpan().ToString("c"),
            element.ZIndex.Value,
            drawable.Id.Value.ToString(),
            drawable.Name.Value,
            drawable.GetType().FullName ?? drawable.GetType().Name,
            drawable.IsEnabled.Value,
            alignmentX.ToString(),
            alignmentY.ToString(),
            "render-node-query-bounds",
            ToBoundsRect(localBounds),
            ToBoundsRect(transformedBounds),
            ToBoundsPoint(transformedBounds.Center),
            userTranslate,
            ToTransformMatrix(userTransform),
            note,
            geometryBoundsOrigin);
    }

    private static RenderNodeBounds MeasureDrawableRenderNodeBounds(
        Drawable drawable,
        Size canvasSize,
        CompositionContext context)
    {
        using var resource = (Drawable.Resource)drawable.ToResource(context);
        using var node = new DrawableRenderNode(resource);
        using (var graphicsContext = new GraphicsContext2D(node, canvasSize, outputScale: 1f))
        {
            drawable.Render(graphicsContext, resource);
        }

        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = new Rect(default, canvasSize),
                OutputScale = 1,
                MaxWorkingScale = 1,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            });
        RenderNodeMeasurement measurement = renderer.Measure();

        return measurement.HasFragments
            ? new RenderNodeBounds(measurement.QueryBounds, null)
            : new RenderNodeBounds(
                Rect.Empty,
                "The drawable produced no contributing RenderNode query fragments at the requested time.");
    }

    private static Rect NormalizeBoundsSize(Rect bounds)
    {
        return new Rect(0, 0, MathF.Max(0f, bounds.Width), MathF.Max(0f, bounds.Height));
    }

    private sealed record RenderNodeBounds(Rect Bounds, string? Note);

    private static ObjectBoundsRect ToBoundsRect(Rect rect)
    {
        return new ObjectBoundsRect(rect.Left, rect.Top, rect.Right, rect.Bottom, rect.Width, rect.Height);
    }

    private static ObjectBoundsPoint ToBoundsPoint(Point point)
    {
        return new ObjectBoundsPoint(point.X, point.Y);
    }

    private static ObjectTransformMatrix ToTransformMatrix(Matrix matrix)
    {
        return new ObjectTransformMatrix(
            matrix.M11,
            matrix.M12,
            matrix.M13,
            matrix.M21,
            matrix.M22,
            matrix.M23,
            matrix.M31,
            matrix.M32,
            matrix.M33);
    }
}
