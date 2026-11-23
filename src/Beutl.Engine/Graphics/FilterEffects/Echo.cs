using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public enum EchoBlendMode
{
    [Display(Name = nameof(GraphicsStrings.EchoBlendMode_NewestOnTop), ResourceType = typeof(GraphicsStrings))]
    NewestOnTop,

    [Display(Name = nameof(GraphicsStrings.EchoBlendMode_Add), ResourceType = typeof(GraphicsStrings))]
    Add,
}

/// <summary>Composites several samples of its input with decreasing opacity.</summary>
[Display(Name = nameof(GraphicsStrings.Echo), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Echo : FilterEffect
{
    public Echo() => ScanProperties<Echo>();

    [Display(Name = nameof(GraphicsStrings.Echo_Interval), ResourceType = typeof(GraphicsStrings))]
    [Range(-60, 60)]
    public IExpressionProperty<float> Interval { get; } = Property.CreateAnimatable(-0.1f);

    [Display(Name = nameof(GraphicsStrings.Echo_Count), ResourceType = typeof(GraphicsStrings))]
    [Range(1, 64)]
    public IExpressionProperty<int> Count { get; } = Property.CreateExpression(4);

    [Display(Name = nameof(GraphicsStrings.Echo_StartingIntensity), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> StartingIntensity { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.Echo_Decay), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    public IExpressionProperty<float> Decay { get; } = Property.CreateAnimatable(70f);

    [Display(Name = nameof(GraphicsStrings.Echo_BlendMode), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<EchoBlendMode> BlendMode { get; } = Property.CreateExpression(EchoBlendMode.NewestOnTop);

    public new partial class Resource
    {
        internal IReadOnlyList<(FilterEffectInputSample Sample, float Opacity)> TimeSamples { get; private set; } = [];

        public override bool UsesInputEvaluation => true;

        protected override void ApplyTo(FilterEffectContext context) { }

        public override bool PrepareInput(FilterEffectInputContext input)
        {
            double interval = float.IsFinite(Interval) ? Math.Clamp(Interval, -60f, 60f) : 0;
            float opacity = ToOpacity(StartingIntensity);
            float decay = ToOpacity(Decay);
            int count = Math.Clamp(Count, 1, 64);
            if ((count == 1 || decay == 0) && opacity == 1) return false;

            var samples = new List<(FilterEffectInputSample, float)>(count);
            for (int index = 0; index < count && opacity > 0; index++)
            {
                MediaTime time = input.EvaluationContext.Time + MediaTime.FromSeconds(index * interval);
                samples.Add((input.Evaluate(time), opacity));
                opacity *= decay;
            }
            TimeSamples = samples;
            return true;
        }

        public override void RenderInput(GraphicsContext2D context, FilterEffectInputContext input)
        {
            var blend = BlendMode == EchoBlendMode.Add ? Graphics.BlendMode.Plus : Graphics.BlendMode.SrcOver;
            for (int index = 0; index < TimeSamples.Count; index++)
            {
                int sampleIndex = Interval < 0 ? TimeSamples.Count - 1 - index : index;
                (FilterEffectInputSample sample, float opacity) = TimeSamples[sampleIndex];
                using (context.PushBlendMode(blend))
                using (context.PushOpacity(opacity))
                    sample.Draw(context);
            }
        }

        partial void PostReconcile(Echo obj, CompositionContext context) => TimeSamples = [];

        partial void PostDispose(bool disposing) => TimeSamples = [];

        private static float ToOpacity(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 100) / 100 : 0;
    }
}
