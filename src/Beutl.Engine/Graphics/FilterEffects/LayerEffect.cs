using System.ComponentModel.DataAnnotations;
using System.Reactive;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.LayerEffect), ResourceType = typeof(GraphicsStrings))]
public partial class LayerEffect : FilterEffect
{
    internal static EffectTarget CreateComposite(CustomFilterEffectContext context)
    {
        Rect bounds = context.Targets.CalculateBounds();
        EffectTarget output = context.CreateTarget(bounds);
        if (output.IsEmpty) return output;

        try
        {
            using var canvas = context.Open(output);
            canvas.Clear();
            foreach (EffectTarget target in context.Targets)
            {
                using (canvas.PushTransform(Matrix.CreateTranslation(target.Bounds.Position - bounds.Position)))
                    target.Draw(canvas);
            }
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            context.CustomEffect(Unit.Default,
                (_, ctx) =>
                {
                    EffectTarget newTarget = CreateComposite(ctx);
                    if (newTarget.IsEmpty)
                    {
                        newTarget.Dispose();
                        return;
                    }

                    ctx.Targets.Clear();
                    ctx.Targets.Add(newTarget);
                },
                // Flattening the targets into their own union never leaves the incoming extent.
                static (_, bounds) => bounds,
                // Flattening moves nothing, so the content stays where it is drawn without the effect.
                CustomFilterEffectOptions.KeepsPlacement);
        }
    }
}
