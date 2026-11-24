using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

/// <summary>Holds the drawable's input between samples spaced from the drawable's start.</summary>
[Display(Name = nameof(GraphicsStrings.PosterizeTime), Description = nameof(GraphicsStrings.PosterizeTime_Description),
    ResourceType = typeof(GraphicsStrings))]
public sealed partial class PosterizeTime : FilterEffect
{
    private const double MinimumRate = 0.01;
    private const float MaximumRate = 240f;

    public PosterizeTime() => ScanProperties<PosterizeTime>();

    [Display(Name = nameof(GraphicsStrings.PosterizeTime_FrameRate),
        Description = nameof(GraphicsStrings.PosterizeTime_FrameRate_Description), ResourceType = typeof(GraphicsStrings))]
    [Range(MinimumRate, MaximumRate)]
    public IExpressionProperty<float> FrameRate { get; } = Property.CreateAnimatable(12f);

    public new partial class Resource
    {
        internal FilterEffectInputSample? Sample { get; private set; }

        public override bool UsesInputEvaluation => true;

        protected override void ApplyTo(FilterEffectContext context) { }

        public override bool PrepareInput(FilterEffectInputContext input)
        {
            if (!float.IsFinite(FrameRate) || FrameRate <= 0) return false;

            Rational rate = ToRate(Math.Clamp(FrameRate, (float)MinimumRate, MaximumRate));
            MediaTime start = input.InputStart;
            MediaTime elapsed = input.EvaluationContext.Time - start;
            MediaTime heldTime = start + elapsed.RoundToFrame(rate, MidpointRounding.ToNegativeInfinity);
            Sample = input.Evaluate(heldTime);
            return true;
        }

        public override void RenderInput(GraphicsContext2D context, FilterEffectInputContext input)
            => Sample?.Draw(context);

        partial void PostReconcile(PosterizeTime obj, CompositionContext context) => Sample = null;

        partial void PostDispose(bool disposing) => Sample = null;

        private static Rational ToRate(float value)
        {
            // The shortest round-trip decimal preserves rates such as 0.01 and 29.97 as entered.
            // Between 0.01 and 240, nine significant digits fit in 16 characters and ten fractional digits suffice.
            const long scale = 10_000_000_000;
            Span<char> text = stackalloc char[16];
            value.TryFormat(text, out int length, provider: CultureInfo.InvariantCulture);
            decimal fps = decimal.Parse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture);
            return new Rational((long)(fps * scale), scale);
        }
    }
}
