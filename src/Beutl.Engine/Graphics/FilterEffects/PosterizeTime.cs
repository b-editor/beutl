using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

/// <summary>Holds the input between successive samples on a grid starting at the drawable's start.</summary>
[Display(Name = nameof(GraphicsStrings.PosterizeTime), ResourceType = typeof(GraphicsStrings))]
public sealed partial class PosterizeTime : FilterEffect
{
    public PosterizeTime() => ScanProperties<PosterizeTime>();

    [Display(Name = nameof(GraphicsStrings.PosterizeTime_FrameRate), ResourceType = typeof(GraphicsStrings))]
    [Range(0.01, 240)]
    public IExpressionProperty<float> FrameRate { get; } = Property.CreateAnimatable(12f);

    public new partial class Resource
    {
        internal FilterEffectInputSample? Sample { get; private set; }

        public override bool UsesInputEvaluation => true;

        protected override void ApplyTo(FilterEffectContext context) { }

        public override bool PrepareInput(FilterEffectInputContext input)
        {
            if (!float.IsFinite(FrameRate) || FrameRate <= 0) return false;

            var rate = Rational.FromDouble(Math.Clamp(FrameRate, 0.01f, 240f));
            MediaTime start = input.InputStart;
            MediaTime time = input.EvaluationContext.Time;
            long frame = (time - start).ToFrame(rate, MidpointRounding.ToNegativeInfinity);
            MediaTime sampleTime = start + MediaTime.FromFrames(frame, rate);
            if (time == sampleTime) return false;

            Sample = input.Evaluate(sampleTime);
            return true;
        }

        public override void RenderInput(GraphicsContext2D context, FilterEffectInputContext input)
            => Sample?.Draw(context);

        partial void PostReconcile(PosterizeTime obj, CompositionContext context) => Sample = null;

        partial void PostDispose(bool disposing) => Sample = null;
    }
}
