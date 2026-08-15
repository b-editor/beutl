using Beutl.Audio.Effects;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using SkiaSharp;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class ObjectTemplatePreviewRendererTests
{
    [Test]
    public async Task RenderPngAsync_Drawable_ProducesADecodablePng()
    {
        byte[]? png = await ObjectTemplatePreviewRenderer.RenderPngAsync(CreateRedRect());

        Assert.That(png, Is.Not.Null);
        using SKBitmap? decoded = SKBitmap.Decode(png);
        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded!.Width, Is.GreaterThan(0));
        Assert.That(decoded.Height, Is.GreaterThan(0));
    }

    // A 100x100 shape inside a 1920x1080 project must fill the thumbnail. Composing the element
    // into the preview's own frame cropped it instead; scaling the whole project frame down would
    // shrink it to a few pixels.
    [Test]
    public async Task RenderPngAsync_Element_FitsTheDrawnContentNotTheProjectFrame()
    {
        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.scene"))
        };
        var element = new Element { Start = TimeSpan.Zero, Length = TimeSpan.FromSeconds(2) };
        element.Objects.Add(CreateRedRect());
        element.Uri = new Uri(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.belm"));
        scene.Children.Add(element);

        byte[]? png = await ObjectTemplatePreviewRenderer.RenderPngAsync(element);

        Assert.That(png, Is.Not.Null);
        using SKBitmap decoded = SKBitmap.Decode(png);

        // The square shape crops to a square preview, and its colour reaches every corner. The crop
        // rounds outward from a fractional origin, so the size can overshoot by a pixel.
        Assert.Multiple(() =>
        {
            Assert.That(decoded.Width, Is.EqualTo(decoded.Height));
            Assert.That(
                decoded.Height,
                Is.EqualTo(ObjectTemplatePreviewRenderer.PreviewHeight).Within(2));
            Assert.That(decoded.GetPixel(2, 2).Alpha, Is.GreaterThan(200));
            Assert.That(decoded.GetPixel(decoded.Width - 3, decoded.Height - 3).Alpha, Is.GreaterThan(200));
        });
    }

    [Test]
    public async Task RenderPngAsync_Element_ProducesAPreviewOfItsDrawables()
    {
        var element = new Element { Start = TimeSpan.FromSeconds(3), Length = TimeSpan.FromSeconds(2) };
        element.Objects.Add(CreateRedRect());

        byte[]? png = await ObjectTemplatePreviewRenderer.RenderPngAsync(element);

        Assert.That(png, Is.Not.Null);
    }

    // The saved object is not itself drawable, so the preview must be the drawable that owns it
    // rather than the generic sample shape.
    [Test]
    public async Task RenderPngAsync_NonDrawable_RendersTheOwningDrawable()
    {
        RectShape owner = CreateRedRect();
        var effect = new FilterEffectGroup();
        owner.FilterEffect.CurrentValue = effect;

        byte[]? png = await ObjectTemplatePreviewRenderer.RenderPngAsync(effect);

        Assert.That(png, Is.Not.Null);
        Assert.That(DominantColor(png!), Is.EqualTo(SKColors.Red).Using<SKColor>(IsCloseTo));
    }

    [Test]
    public async Task RenderPngAsync_NonDrawableWithoutOwner_FallsBackToASampleShape()
    {
        byte[]? png = await ObjectTemplatePreviewRenderer.RenderPngAsync(
            new SolidColorBrush(Colors.Lime));

        Assert.That(png, Is.Not.Null);
        Assert.That(DominantColor(png!), Is.EqualTo(SKColors.Lime).Using<SKColor>(IsCloseTo));
    }

    [Test]
    public async Task RenderPngAsync_DoesNotReparentTheLiveObject()
    {
        RectShape owner = CreateRedRect();
        var effect = new FilterEffectGroup();
        owner.FilterEffect.CurrentValue = effect;
        var brush = new SolidColorBrush(Colors.Lime);

        await ObjectTemplatePreviewRenderer.RenderPngAsync(effect);
        await ObjectTemplatePreviewRenderer.RenderPngAsync(brush);

        Assert.That(effect.HierarchicalParent, Is.SameAs(owner));
        Assert.That(brush.HierarchicalParent, Is.Null);
    }

    [Test]
    public async Task RenderPngAsync_AudioEffect_HasNoPreview()
    {
        byte[]? png = await ObjectTemplatePreviewRenderer.RenderPngAsync(new AudioEffectGroup());

        Assert.That(png, Is.Null);
    }

    // An element that only makes sound composites to a transparent frame; embedding that would
    // replace a meaningful type icon with an empty square.
    [Test]
    public async Task RenderPngAsync_ElementWithNothingVisible_HasNoPreview()
    {
        var element = new Element { Start = TimeSpan.Zero, Length = TimeSpan.FromSeconds(1) };

        byte[]? png = await ObjectTemplatePreviewRenderer.RenderPngAsync(element);

        Assert.That(png, Is.Null);
    }

    private static RectShape CreateRedRect()
    {
        return new RectShape
        {
            Width = { CurrentValue = 100 },
            Height = { CurrentValue = 100 },
            Fill = { CurrentValue = new SolidColorBrush(Colors.Red) }
        };
    }

    private static SKColor DominantColor(byte[] png)
    {
        using SKBitmap bitmap = SKBitmap.Decode(png);
        var counts = new Dictionary<SKColor, int>();
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor color = bitmap.GetPixel(x, y);
                if (color.Alpha < 128) continue;
                counts[color] = counts.GetValueOrDefault(color) + 1;
            }
        }

        return counts.Count == 0
            ? SKColors.Transparent
            : counts.MaxBy(kv => kv.Value).Key;
    }

    // The renderer round-trips through linear F16 and back to sRGB, so channels land within a
    // rounding step of the authored colour rather than exactly on it.
    private static bool IsCloseTo(SKColor actual, SKColor expected)
    {
        return Math.Abs(actual.Red - expected.Red) <= 2
               && Math.Abs(actual.Green - expected.Green) <= 2
               && Math.Abs(actual.Blue - expected.Blue) <= 2;
    }
}
