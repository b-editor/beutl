using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

internal static class RenderCacheTestSupport
{
    public static RenderOutputCacheIdentity CreateCacheIdentity(
        Rect bounds,
        string name = "test-cache",
        float density = 1)
    {
        var fragment = new RenderFragmentReference(
            RenderFragmentKind.Layer,
            bounds,
            EffectiveScale.At(density),
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            inputs: [],
            payload: null,
            hitTest: RenderFragmentHitTest.None);
        return new RenderOutputCacheIdentity(
            name,
            RenderFragmentOutputIdentity.Create(fragment, new RenderRequestId(1)),
            bounds,
            RequiredRegion.Region(bounds),
            density,
            RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            FusionMode.Enabled,
            new RenderCacheDeviceContextIdentity("test-device", "test-context"));
    }

    /// <summary>
    /// A hit or miss-capture decision for <paramref name="candidate"/>, with an identity over <paramref name="bounds"/>.
    /// </summary>
    public static RenderCacheDecision CreateDecision(
        RenderCacheCandidate candidate,
        RenderCacheResolutionKind kind,
        Rect bounds)
        => kind switch
        {
            RenderCacheResolutionKind.Hit => new RenderCacheDecision(
                candidate,
                kind,
                null,
                new RenderCacheEntry(CreateCacheIdentity(bounds), new object())),
            RenderCacheResolutionKind.MissCapture => new RenderCacheDecision(
                candidate,
                kind,
                CreateCacheIdentity(bounds),
                null),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public static RenderNodeCachePublication CreatePublication(
        RenderNodeCache cache,
        RenderTarget target,
        Rect bounds,
        string name = "test-cache")
    {
        return new RenderNodeCachePublication(
            cache,
            CreateCacheIdentity(bounds, name),
            [new RenderNodeCachedValue(target, bounds, EffectiveScale.At(1))]);
    }

    /// <summary>Drives a cache to the stable-request count at which it may capture.</summary>
    public static void RecordStableRequests(
        this RenderNodeCache cache,
        int count = RenderNodeCache.StableRequestCount)
    {
        for (int index = 0; index < count; index++)
            cache.RecordSuccessfulStableRequest();
    }

    /// <summary>
    /// Clears the changes a freshly built tree reports, so a fixture starts in the settled state a renderer
    /// reaches one frame later.
    /// </summary>
    public static void SettleConstruction(this RenderNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.ClearChanges(node.ChangeVersion);
        foreach (RenderNode child in node.ChildNodes.ToArray())
            child.SettleConstruction();
    }

    /// <summary>A 240x160 background, a brightened ellipse and a text label, composed by the cache tests.</summary>
    public static Drawable.Resource[] CreateRepresentativeScene()
    {
        var background = new RectShape
        {
            Width = { CurrentValue = 240 },
            Height = { CurrentValue = 160 },
            Fill = { CurrentValue = Brushes.CornflowerBlue },
        };

        var accent = new EllipseShape
        {
            Width = { CurrentValue = 76 },
            Height = { CurrentValue = 76 },
            Fill = { CurrentValue = Brushes.OrangeRed },
            FilterEffect = { CurrentValue = new Brightness { Amount = { CurrentValue = 78 } } },
            Transform = { CurrentValue = new TranslateTransform(44, -18) },
        };

        var label = new TextBlock
        {
            FontFamily = { CurrentValue = FontFamily.Default },
            Size = { CurrentValue = 28 },
            Fill = { CurrentValue = Brushes.White },
            Text = { CurrentValue = "CACHE" },
            Transform = { CurrentValue = new TranslateTransform(-28, 30) },
        };

        CompositionContext context = CompositionContext.Default;
        return [background.ToResource(context), accent.ToResource(context), label.ToResource(context)];
    }
}
