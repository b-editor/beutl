using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Shaders;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.RangeAnimatorEffect), Description = nameof(GraphicsStrings.RangeAnimatorEffect_LibraryDescription),
    ResourceType = typeof(GraphicsStrings))]
public sealed partial class RangeAnimatorEffect : FilterEffect
{
    public RangeAnimatorEffect()
    {
        ScanProperties<RangeAnimatorEffect>();
    }

    [Display(Name = nameof(GraphicsStrings.RangeAnimatorEffect_Start), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    [Unit(PropertyUnit.Percent)]
    public IExpressionProperty<float> RangeStart { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.RangeAnimatorEffect_End), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    [Unit(PropertyUnit.Percent)]
    public IExpressionProperty<float> RangeEnd { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.Offset), ResourceType = typeof(GraphicsStrings))]
    [Range(-100, 100)]
    [Unit(PropertyUnit.Percent)]
    public IExpressionProperty<float> RangeOffset { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.TextAnimationUnit), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<TextAnimationUnit> Unit { get; } = Property.CreateAnimatable(TextAnimationUnit.Characters);

    [Display(Name = nameof(GraphicsStrings.DelayAnimationEffect_Order), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<DelayAnimationOrder> Order { get; } = Property.CreateAnimatable(DelayAnimationOrder.Forward);

    [Display(Name = nameof(GraphicsStrings.DelayAnimationEffect_RandomSeed), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<int> RandomSeed { get; } = Property.CreateAnimatable(0);

    [Display(Name = nameof(GraphicsStrings.RangeAnimatorEffect_Shape), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<RangeAnimatorShape> Shape { get; } = Property.CreateAnimatable(RangeAnimatorShape.Square);

    [Display(Name = nameof(GraphicsStrings.RangeAnimatorEffect_Smoothness), Description = nameof(GraphicsStrings.RangeAnimatorEffect_Smoothness_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    [Unit(PropertyUnit.Percent)]
    public IExpressionProperty<float> Smoothness { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.Position), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Point> Position { get; } = Property.CreateAnimatable(default(Point));

    [Display(Name = nameof(GraphicsStrings.Scale), ResourceType = typeof(GraphicsStrings))]
    [Unit(PropertyUnit.Percent)]
    public IExpressionProperty<float> Scale { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.Rotation), ResourceType = typeof(GraphicsStrings))]
    [Unit(PropertyUnit.Degrees)]
    public IExpressionProperty<float> Rotation { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.Opacity), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    [Unit(PropertyUnit.Percent)]
    public IExpressionProperty<float> Opacity { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.Blur), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Blur { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.Color), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> Color { get; } = Property.CreateAnimatable(Colors.White);

    [Display(Name = nameof(GraphicsStrings.RangeAnimatorEffect_ColorAmount), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    [Unit(PropertyUnit.Percent)]
    public IExpressionProperty<float> ColorAmount { get; } = Property.CreateAnimatable(0f);

    private static readonly SkslSource s_tintSource = new(
        """
        uniform float3 tint;
        uniform float amount;

        half4 apply(half4 color) {
            float4 pixel = float4(color);
            return half4(mix(pixel.rgb, tint * pixel.a, amount), pixel.a);
        }
        """,
        ShaderDescriptionKind.CurrentPixel);

    private readonly record struct Settings(
        float RangeStart, float RangeEnd, float RangeOffset, TextAnimationUnit Unit, DelayAnimationOrder Order, int RandomSeed,
        RangeAnimatorShape Shape, float Smoothness, Point Position, float Scale, float Rotation,
        float Opacity, float Blur, Vector3 Tint, float ColorAmount);

    private static void ApplySelected(Settings data, CustomFilterEffectContext effectContext)
    {
        int count = effectContext.Targets.Count;
        Span<float> amounts = count <= 128 ? stackalloc float[count] : new float[count];
        DelayAnimationOrdering.ComputeTargetRanks(data.Order, data.RandomSeed, effectContext.Targets, amounts, data.Unit);
        RangeAnimatorSelector.ConvertRanksToAmounts(amounts, data.RangeStart, data.RangeEnd, data.RangeOffset, data.Shape, data.Smoothness);
        Dictionary<TextPartTag, Rect>? characterBounds = data.Scale != 1 || data.Rotation != 0
            ? GetCharacterBounds(effectContext.Targets)
            : null;

        // Work backwards so a part scaled to zero can be removed without moving the remaining indices.
        for (int i = count - 1; i >= 0; i--)
        {
            float amount = amounts[i];
            if (amount == 0) continue;

            EffectTarget target = effectContext.Targets[i];
            if (target.IsEmpty) continue;

            Point center = characterBounds is not null && target.TextPart is { } text
                && characterBounds.TryGetValue(text, out Rect bounds)
                ? bounds.Center
                : target.Bounds.Center;
            using var context = new FilterEffectContext(target.Bounds, effectContext.OutputScale, effectContext.WorkingScale);
            ApplyPart(context, data, amount, center);

            using var targets = new EffectTargets { target.Clone() };
            targets[0].OriginalBounds = target.Bounds.WithX(0).WithY(0);
            using var builder = new SKImageFilterBuilder();
            using FilterEffectExecutor executor = effectContext.CreateExecutor(targets, builder);
            executor.Apply(context);
            executor.Flush(false);

            if (targets.Count > 0)
                effectContext.Targets[i] = targets.DetachAt(0);
            else
                effectContext.Targets.RemoveAt(i);
        }
    }

    private static Dictionary<TextPartTag, Rect>? GetCharacterBounds(EffectTargets targets)
    {
        Dictionary<TextPartTag, Rect>? characters = null;
        foreach (EffectTarget target in targets)
        {
            if (target.IsEmpty || target.TextPart is not { Indices.Character: >= 0 } text)
                continue;

            // Stroke and fill can have different bounds. Rotate and scale both around their shared center.
            characters ??= new();
            Rect bounds = target.CompleteBounds ?? target.Bounds;
            characters[text] = characters.TryGetValue(text, out Rect previous) ? previous.Union(bounds) : bounds;
        }

        return characters;
    }

    private static void ApplyPart(FilterEffectContext context, Settings data, float amount, Point center)
    {
        float scale = (1 - amount) + amount * data.Scale;
        float rotation = amount * data.Rotation;
        float x = amount * data.Position.X;
        float y = amount * data.Position.Y;

        // Transform first so blur stays in output pixels. Tint the softened part, then fade the whole result.
        if (scale != 1 || rotation != 0 || x != 0 || y != 0)
        {
            Matrix matrix = Matrix.CreateTranslation(-center.X, -center.Y)
                * Matrix.CreateScale(scale, scale)
                * Matrix.CreateRotation(rotation * (MathF.PI / 180))
                * Matrix.CreateTranslation(center.X + x, center.Y + y);
            context.Transform(matrix, BitmapInterpolationMode.MediumQuality);
        }

        float blur = amount * data.Blur;
        if (blur > 0)
            context.Blur(new Size(blur, blur));

        float tintAmount = amount * data.ColorAmount;
        if (tintAmount > 0)
        {
            context.Shader(ShaderDescription.CurrentPixel(s_tintSource, bindings =>
            {
                bindings.Uniform("tint", data.Tint);
                bindings.Uniform("amount", tintAmount);
            }));
        }

        context.Opacity((1 - amount) + amount * data.Opacity);
    }

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            Point position = float.IsFinite(Position.X) && float.IsFinite(Position.Y) ? Position : default;
            float scale = float.IsFinite(Scale) ? Scale / 100f : 1f;
            float rotation = float.IsFinite(Rotation) ? Rotation : 0f;
            float opacity = float.IsFinite(Opacity) ? Math.Clamp(Opacity / 100f, 0, 1) : 1f;
            float blur = float.IsFinite(Blur) ? Math.Max(Blur, 0) : 0;
            float colorAmount = float.IsFinite(ColorAmount) ? Math.Clamp(ColorAmount / 100f, 0, 1) : 0;
            if (position == default && scale == 1 && rotation == 0 && opacity == 1 && blur == 0 && colorAmount == 0)
                return;

            Vector4 color = Color.ToLinear();
            context.CustomEffect(
                new Settings(RangeStart, RangeEnd, RangeOffset, Unit, Order, RandomSeed, Shape, Smoothness,
                    position, scale, rotation, opacity, blur, new Vector3(color.X, color.Y, color.Z), colorAmount),
                ApplySelected,
                CustomFilterEffectOptions.AcceptsMaterializedTargets);
        }
    }
}

internal static class RangeAnimatorSelector
{
    internal static void ConvertRanksToAmounts(
        Span<float> ranks, float start, float end, float offset, RangeAnimatorShape shape, float smoothness)
    {
        float maxRank = 0;
        foreach (float rank in ranks)
            maxRank = Math.Max(maxRank, rank);

        start = float.IsFinite(start) ? Math.Clamp(start, 0, 100) : 0;
        end = float.IsFinite(end) ? Math.Clamp(end, 0, 100) : 100;
        offset = float.IsFinite(offset) ? Math.Clamp(offset, -100, 100) : 0;
        double slotCount = (double)maxRank + 1;
        double selectionStart = (Math.Min(start, end) + (double)offset) / 100 * slotCount;
        double selectionEnd = (Math.Max(start, end) + (double)offset) / 100 * slotCount;
        double edgeWidth = float.IsFinite(smoothness) ? Math.Clamp(smoothness, 0, 100) / 100d : 1;

        foreach (ref float rank in ranks)
            rank = rank < 0 ? 0 : (float)GetAmount(rank + 0.5, selectionStart, selectionEnd, shape, edgeWidth);
    }

    private static double GetAmount(double center, double start, double end, RangeAnimatorShape shape, double edgeWidth)
    {
        if (shape == RangeAnimatorShape.Square)
        {
            if (edgeWidth == 0)
                return center >= start && center < end ? 1 : 0;

            // Average the square over the smoothing window. A full slot gives its covered fraction.
            double halfWidth = edgeWidth / 2;
            double covered = Math.Min(end, center + halfWidth) - Math.Max(start, center - halfWidth);
            return Math.Clamp(covered / edgeWidth, 0, 1);
        }

        double t = start == end ? (center < start ? 0 : 1) : Math.Clamp((center - start) / (end - start), 0, 1);
        double centered = 2 * t - 1;
        return shape switch
        {
            RangeAnimatorShape.RampUp => t,
            RangeAnimatorShape.RampDown => 1 - t,
            RangeAnimatorShape.Triangle => 1 - Math.Abs(centered),
            RangeAnimatorShape.Round => Math.Sqrt(1 - centered * centered),
            RangeAnimatorShape.Smooth => (1 - Math.Cos(Math.Tau * t)) / 2,
            _ => 0,
        };
    }
}
