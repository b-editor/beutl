using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Averages explicit time samples of its drawable, including transforms, opacity, media and preceding
/// effects. Following effects run once on the average in the drawable's parent coordinate system.
/// The drawable's blend mode composites that result onto its destination once.
/// </summary>
[Display(Name = nameof(GraphicsStrings.MotionBlur), Description = nameof(GraphicsStrings.MotionBlur_Description),
    ResourceType = typeof(GraphicsStrings))]
public sealed partial class MotionBlur : FilterEffect
{
    public MotionBlur() => ScanProperties<MotionBlur>();

    [Display(Name = nameof(GraphicsStrings.MotionBlur_ShutterAngle), Description = nameof(GraphicsStrings.MotionBlur_ShutterAngle_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, 3600)]
    public IExpressionProperty<float> ShutterAngle { get; } = Property.CreateAnimatable(180f);

    [Display(Name = nameof(GraphicsStrings.MotionBlur_ShutterPosition), Description = nameof(GraphicsStrings.MotionBlur_ShutterPosition_Description),
        ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<ShutterPosition> ShutterPosition { get; } = Property.CreateExpression(Effects.ShutterPosition.Center);

    [Display(Name = nameof(GraphicsStrings.MotionBlur_Samples), Description = nameof(GraphicsStrings.MotionBlur_Samples_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(1, 64)]
    public IExpressionProperty<int> Samples { get; } = Property.CreateExpression(8);

    [Display(Name = nameof(GraphicsStrings.MotionBlur_SourceMix), Description = nameof(GraphicsStrings.MotionBlur_SourceMix_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> SourceMix { get; } = Property.CreateAnimatable(0f);

    public new partial class Resource
    {
        private FilterEffectInputSample? _sourceSample;
        private float _sourceWeight;

        internal IReadOnlyList<FilterEffectInputSample> TimeSamples { get; private set; } = [];

        public override bool UsesInputEvaluation => true;

        // Time sampling is recorded by RenderInput, before the drawable's current-time transform.
        // A context containing only pixels has no animation to evaluate.
        protected override void ApplyTo(FilterEffectContext context)
        {
        }

        partial void PostReconcile(MotionBlur obj, CompositionContext context) => ClearSamples();

        public override bool PrepareInput(FilterEffectInputContext input)
        {
            double angle = float.IsFinite(ShutterAngle) ? Math.Clamp(ShutterAngle, 0, 3600) : 0;
            _sourceWeight = float.IsFinite(SourceMix) ? Math.Clamp(SourceMix, 0, 100) / 100 : 0;
            int count = angle == 0 || _sourceWeight == 1 ? 1 : Math.Clamp(Samples, 1, 64);
            if (count == 1) return false;

            CompositionContext context = input.EvaluationContext;
            double rate = context.FrameRate.ToDouble();
            if (!double.IsFinite(rate) || rate <= 0) rate = CompositionConditions.DefaultFrameRate.ToDouble();
            double exposure = angle / 360 / rate;
            double reference = ShutterPosition switch
            {
                Effects.ShutterPosition.Start => 0,
                Effects.ShutterPosition.End => 1,
                _ => 0.5,
            };
            var samples = new FilterEffectInputSample[count];
            for (int i = 0; i < count; i++)
            {
                MediaTime time = context.Time + MediaTime.FromSeconds(((i + 0.5) / count - reference) * exposure);
                samples[i] = input.Evaluate(time);
            }
            TimeSamples = samples;
            if (_sourceWeight > 0) _sourceSample = input.Evaluate(context.Time);
            return true;
        }

        public override void RenderInput(GraphicsContext2D context, FilterEffectInputContext input)
        {
            float sampleWeight = (1 - _sourceWeight) / TimeSamples.Count;
            foreach (FilterEffectInputSample sample in TimeSamples)
                DrawSample(sample, sampleWeight);
            if (_sourceSample != null)
                DrawSample(_sourceSample, _sourceWeight);

            void DrawSample(FilterEffectInputSample sample, float weight)
            {
                using (context.PushBlendMode(BlendMode.Plus))
                using (context.PushOpacity(weight))
                    sample.Draw(context);
            }
        }

        partial void PostDispose(bool disposing) => ClearSamples();

        private void ClearSamples()
        {
            TimeSamples = [];
            _sourceSample = null;
        }
    }
}
