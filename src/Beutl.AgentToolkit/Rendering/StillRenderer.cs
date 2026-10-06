using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Rendering;

public sealed partial class StillRenderer
{
    public async ValueTask<RenderStillResponse> RenderAsync(
        Scene scene,
        TimeSpan time,
        string outputPath,
        float renderScale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using Bitmap snapshot = await RenderBitmapAsync(
            scene,
            time,
            renderScale,
            cancellationToken).ConfigureAwait(false);

        if (!snapshot.Save(outputPath, EncodedImageFormat.Png))
        {
            throw new IOException($"Failed to write still image to '{outputPath}'.");
        }

        StillFrameVisibilityAnalysis analysis = AnalyzeFrameVisibility(snapshot);
        return new RenderStillResponse(
            outputPath,
            snapshot.Width,
            snapshot.Height,
            time.ToString("c"),
            analysis.Warnings,
            analysis,
            CreateActiveElementSummaries(scene, time));
    }

    public ValueTask<Bitmap> RenderBitmapAsync(
        Scene scene,
        TimeSpan time,
        float renderScale,
        CancellationToken cancellationToken)
    {
        return RenderRenderedFrameAsync(
            scene,
            time,
            renderScale,
            static (_, renderer, _) => renderer.Snapshot(),
            cancellationToken);
    }

    public ValueTask<RenderedFrameAnalysis> RenderFrameAnalysisAsync(
        Scene scene,
        TimeSpan time,
        float renderScale,
        CancellationToken cancellationToken)
    {
        return RenderRenderedFrameAsync(
            scene,
            time,
            renderScale,
            static (renderedScene, renderer, renderedTime) =>
            {
                IReadOnlyList<RenderedTextBounds> textBounds =
                    CreateRenderedTextBounds(renderedScene, renderer, renderedTime);
                return new RenderedFrameAnalysis(renderedTime, renderer.Snapshot(), textBounds);
            },
            cancellationToken);
    }

    private static async ValueTask<TResult> RenderRenderedFrameAsync<TResult>(
        Scene scene,
        TimeSpan time,
        float renderScale,
        Func<Scene, SceneRenderer, TimeSpan, TResult> project,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (ContainsGpuOnlyContent(scene, time)
            && !await Has3DGraphicsContextAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new RenderingUnavailableException(
                "The scene contains 3D content, but no GPU context with 3D rendering support is available.");
        }

        float normalizedScale = float.IsFinite(renderScale) && renderScale > 0f ? renderScale : 1f;
        return await RenderThread.Dispatcher.InvokeAsync(() =>
        {
            using var renderer = ExportRendererFactory.Create(scene, normalizedScale);

            ThrowIfSourcesMissing(scene, time + scene.Start);
            var frame = renderer.Compositor.EvaluateGraphics(time + scene.Start);
            renderer.Render(frame);
            return project(scene, renderer, time);
        }, ct: cancellationToken).ConfigureAwait(false);
    }

    // relativeTime scopes the check to elements active at that scene-relative sample; null checks the
    // whole visible window [Scene.Start, Scene.Start + Duration) (exports render the full window).
    // A Scene3D on a disabled or never-rendered element must not force the GPU requirement.
    internal static bool ContainsGpuOnlyContent(Scene scene, TimeSpan? relativeTime = null)
    {
        TimeSpan windowStart = scene.Start;
        TimeSpan windowEnd = scene.Start + scene.Duration;
        return scene.Children
            .Where(element => element.IsEnabled)
            .Where(element => relativeTime is { } time
                ? element.Range.Contains(time + scene.Start)
                : scene.Duration <= TimeSpan.Zero
                  || (element.Start < windowEnd && element.Start + element.Length > windowStart))
            .SelectMany(element => element.Objects)
            .Any(ContainsEnabledGpuContent);
    }

    // A final render forces original media (no proxy fallback), so a moved/deleted original that a Ready
    // proxy would have stood in for during preview renders a blank frame instead of failing. Preflight the
    // frame's renderable sources on the render thread and fail fast, matching the save-frame/export guard.
    private static void ThrowIfSourcesMissing(Scene scene, TimeSpan sceneTime)
    {
        IReadOnlyList<string> missing = Beutl.Editor.ExportSourceValidator.GetMissingPaths(
            Beutl.Editor.ExportSourceValidator.CollectRenderableSources(scene, sceneTime));
        if (missing.Count > 0)
        {
            throw new RenderingUnavailableException(
                $"Missing source files required to render: {string.Join(", ", missing)}");
        }
    }

    // The renderer skips a disabled object (EngineObject.IsEnabled) and everything under it, so a
    // disabled Scene3D — or a Scene3D under a disabled group — must not force the GPU requirement.
    // EnumerateAllChildren walks disabled subtrees, so recurse manually and prune them.
    private static bool ContainsEnabledGpuContent(IHierarchical node)
    {
        return ContainsEnabledGpuContent(node, new HashSet<IHierarchical>(ReferenceEqualityComparer.Instance));
    }

    private static bool ContainsEnabledGpuContent(IHierarchical node, HashSet<IHierarchical> visited)
    {
        if (!visited.Add(node))
        {
            return false;
        }

        if (node is EngineObject { IsEnabled: false })
        {
            return false;
        }

        if (node is Scene3D)
        {
            return true;
        }

        foreach (IHierarchical child in node.HierarchicalChildren)
        {
            if (ContainsEnabledGpuContent(child, visited))
            {
                return true;
            }
        }

        // A referenced scene is not a hierarchical child and its ReferenceExpression resolves only
        // at composition time, so its GPU requirement is invisible unless the expression target is
        // followed here (visited-guarded because references are user-cyclable). Only a Drawable
        // owner evaluates the referenced scene's graphics — an audio-only SceneSound must not. Follow
        // only the known scene-reference properties: ReferenceExpression is a general binding form, so
        // an arbitrary data-binding on some other property must not drag in unrelated GPU content. The
        // PropertyPath form is rejected at apply time, so only a direct-ObjectId target is followed,
        // and only when it is the referenced type — ReferenceExpression<Scene?> evaluates a non-Scene
        // target to null, so a legacy/malformed Id resolving to another object must not be followed.
        if (node is Drawable drawable && drawable.FindHierarchicalRoot() is ICoreObject lookupRoot)
        {
            foreach (IProperty property in drawable.Properties)
            {
                if (Common.ReferenceProperties.Describe(property) is { } descriptor
                    && property.Expression is IReferenceExpression { HasPropertyPath: false } referenceExpression
                    && lookupRoot.FindById(referenceExpression.ObjectId) is IHierarchical expressionTarget
                    && descriptor.ReferencedType.IsInstanceOfType(expressionTarget)
                    && ContainsEnabledGpuContent(expressionTarget, visited))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static async ValueTask<bool> Has3DGraphicsContextAsync(CancellationToken cancellationToken)
    {
        return await RenderThread.Dispatcher.InvokeAsync(
            () => GraphicsContextFactory.GetOrCreateShared()?.Supports3DRendering == true,
            ct: cancellationToken).ConfigureAwait(false);
    }

    internal static IReadOnlyList<RenderStillActiveElement> CreateActiveElementSummaries(Scene scene, TimeSpan time)
    {
        // The frame is rendered at time + scene.Start (EvaluateGraphics), so element activity must be
        // filtered at the same absolute time or a non-zero Scene.Start reports the wrong elements.
        TimeSpan renderTime = time + scene.Start;
        return scene.Children
            .Where(element => element.IsEnabled && element.Range.Contains(renderTime))
            .OrderBy(element => element.ZIndex)
            .Select(element => new RenderStillActiveElement(
                element.Id.ToString(),
                element.Name,
                element.Start.ToString("c"),
                element.Length.ToString("c"),
                element.ZIndex,
                element.Objects.Count))
            .ToArray();
    }

    private static IReadOnlyList<RenderedTextBounds> CreateRenderedTextBounds(
        Scene scene,
        SceneRenderer renderer,
        TimeSpan time)
    {
        TimeSpan renderTime = time + scene.Start;
        var result = new List<RenderedTextBounds>();
        foreach (Element element in scene.Children)
        {
            if (!element.IsEnabled || !element.Range.Contains(renderTime))
            {
                continue;
            }

            foreach (EngineObject obj in element.Objects)
            {
                CollectRenderedTextBounds(element, obj, renderer, result);
            }
        }

        return result;
    }

    // Flow operators (DrawableGroup / DrawableDecorator) render their children, so text nested under
    // a group (e.g. after duplicate_object(wrapInGroup:true)) must still be measured — otherwise
    // rendered contrast / eye-trace checks ignore grouped text that is actually on screen.
    private static void CollectRenderedTextBounds(
        Element element,
        EngineObject obj,
        SceneRenderer renderer,
        List<RenderedTextBounds> result)
    {
        if (obj is TextBlock textBlock && textBlock.IsEnabled
            && renderer.GetBoundary(textBlock) is { } bounds
            && bounds.Width > 0
            && bounds.Height > 0)
        {
            // Attribute the bound to the TextBlock's own owning element, not the outer loop element:
            // a grouped/portaled child can originate from a different element, and downstream
            // focal-point selection ranks by RenderedTextBounds.Element.ZIndex.
            Element owner = textBlock.FindHierarchicalParent<Element>() ?? element;
            result.Add(new RenderedTextBounds(owner, textBlock, bounds));
        }

        IEnumerable<EngineObject> children = obj switch
        {
            DrawableGroup group => group.Children,
            DrawableDecorator decorator => decorator.Children,
            _ => []
        };

        foreach (EngineObject child in children)
        {
            CollectRenderedTextBounds(element, child, renderer, result);
        }
    }
}
