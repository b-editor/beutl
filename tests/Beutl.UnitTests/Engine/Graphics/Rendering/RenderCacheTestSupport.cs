using System.Reflection;
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
            [new RenderNodeCachedValue(target, bounds, EffectiveScale.At(1), PixelRect.FromRect(bounds, 1))]);
    }

    /// <summary>
    /// Reads back the identity the cache last published. The cache shares it only with the renderer that
    /// resolved it, so a test reads it here to check it or to look the published values up through the
    /// production lookup.
    /// </summary>
    public static RenderOutputCacheIdentity CurrentCacheIdentity(this RenderNodeCache cache)
    {
        object storage = typeof(RenderNodeCache)
            .GetField("_storage", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(cache)!;
        return (RenderOutputCacheIdentity)storage.GetType().GetProperty("Identity")!.GetValue(storage)!;
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
    /// Asserts that a cache has recorded no stable request yet: it admits a capture only after all
    /// <see cref="RenderNodeCache.StableRequestCount"/> of them. It records those requests, so call it where the
    /// rest of the test no longer reads this cache's warm-up.
    /// </summary>
    public static void AssertWarmupRestarted(this RenderNodeCache cache)
    {
        for (int recorded = 1; recorded < RenderNodeCache.StableRequestCount; recorded++)
        {
            cache.RecordSuccessfulStableRequest();
            Assert.That(cache.CanCapture, Is.False, $"capture admitted after {recorded} stable requests");
        }

        cache.RecordSuccessfulStableRequest();
        Assert.That(cache.CanCapture, Is.True);
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
            Width = { Value = 240 },
            Height = { Value = 160 },
            Fill = { Value = Brushes.CornflowerBlue },
        };

        var accent = new EllipseShape
        {
            Width = { Value = 76 },
            Height = { Value = 76 },
            Fill = { Value = Brushes.OrangeRed },
            FilterEffect = { Value = new Brightness { Amount = { Value = 78 } } },
            Transform = { Value = new TranslateTransform(44, -18) },
        };

        var label = new TextBlock
        {
            FontFamily = { Value = FontFamily.Default },
            Size = { Value = 28 },
            Fill = { Value = Brushes.White },
            Text = { Value = "CACHE" },
            Transform = { Value = new TranslateTransform(-28, 30) },
        };

        CompositionContext context = CompositionContext.Default;
        return [background.ToResource(context), accent.ToResource(context), label.ToResource(context)];
    }
}
