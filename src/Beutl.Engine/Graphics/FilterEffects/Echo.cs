using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Echo), Description = nameof(GraphicsStrings.Echo_Description),
    ResourceType = typeof(GraphicsStrings))]
public sealed partial class Echo : FilterEffect
{
    public Echo() => ScanProperties<Echo>();

    [Display(Name = nameof(GraphicsStrings.Echo_Interval), Description = nameof(GraphicsStrings.Echo_Interval_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(-60, 60)]
    public IExpressionProperty<float> Interval { get; } = Property.CreateAnimatable(-0.1f);

    [Display(Name = nameof(GraphicsStrings.Echo_Count), Description = nameof(GraphicsStrings.Echo_Count_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(1, 64)]
    public IExpressionProperty<int> Count { get; } = Property.CreateExpression(4);

    [Display(Name = nameof(GraphicsStrings.Echo_StartingIntensity), Description = nameof(GraphicsStrings.Echo_StartingIntensity_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    [Unit(PropertyUnit.Percent)]
    public IExpressionProperty<float> StartingIntensity { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.Echo_Decay), Description = nameof(GraphicsStrings.Echo_Decay_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    [Unit(PropertyUnit.Percent)]
    public IExpressionProperty<float> Decay { get; } = Property.CreateAnimatable(70f);

    [Display(Name = nameof(GraphicsStrings.Echo_BlendMode), Description = nameof(GraphicsStrings.Echo_BlendMode_Description),
        ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<EchoBlendMode> BlendMode { get; } = Property.CreateExpression(EchoBlendMode.Add);

    public new partial class Resource
    {
        // The copies left out may add up to this opacity, which moves an 8-bit output by under half a step even over
        // black, where sRGB is 12.92 times steeper than linear light.
        private const float NegligibleOpacity = 0.5f / 255f / 12.92f;

        private bool _drawInReverse;

        internal IReadOnlyList<(FilterEffectInputSample Sample, float Opacity)> TimeSamples { get; private set; } = [];

        public override bool UsesInputEvaluation => true;

        protected override void ApplyTo(FilterEffectContext context)
        {
        }

        partial void PostReconcile(Echo obj, CompositionContext context) => ClearSamples();

        public override bool PrepareInput(FilterEffectInputContext input)
        {
            double interval = float.IsFinite(Interval) ? Math.Clamp(Interval, -60f, 60f) : 0;
            float opacity = ToOpacity(StartingIntensity);
            float decay = ToOpacity(Decay);
            int count = Math.Clamp(Count, 1, 64);
            if ((count == 1 || decay == 0) && opacity == 1) return false;

            _drawInReverse = interval < 0;
            Span<float> opacities = stackalloc float[count];
            for (int index = 0; index < count; index++, opacity *= decay)
                opacities[index] = opacity;

            // Whether drawn over or added to the others, a copy changes the output by at most its opacity, so the
            // faintest copies together change it by at most the sum of theirs. Skip evaluating those.
            float omitted = 0;
            while (count > 0 && omitted + opacities[count - 1] < NegligibleOpacity)
                omitted += opacities[--count];

            var samples = new List<(FilterEffectInputSample Sample, float Opacity)>(count);
            for (int index = 0; index < count; index++)
            {
                MediaTime time = input.EvaluationContext.Time + MediaTime.FromSeconds(index * interval);
                samples.Add((input.Evaluate(time), opacities[index]));
            }
            TimeSamples = samples;
            return true;
        }

        public override void RenderInput(GraphicsContext2D context, FilterEffectInputContext input)
        {
            var blend = BlendMode == EchoBlendMode.Add ? Graphics.BlendMode.Plus : Graphics.BlendMode.SrcOver;
            if (_drawInReverse)
            {
                for (int index = TimeSamples.Count - 1; index >= 0; index--)
                    DrawSample(TimeSamples[index]);
            }
            else
            {
                foreach (var sample in TimeSamples)
                    DrawSample(sample);
            }

            void DrawSample((FilterEffectInputSample Sample, float Opacity) copy)
            {
                using (context.PushBlendMode(blend))
                using (context.PushOpacity(copy.Opacity))
                    copy.Sample.Draw(context);
            }
        }

        partial void PostDispose(bool disposing) => ClearSamples();

        private void ClearSamples()
        {
            TimeSamples = [];
            _drawInReverse = false;
        }

        private static float ToOpacity(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 100) / 100f : 0f;
    }
}
