using System.ComponentModel.DataAnnotations;
using System.Reactive;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.LayerEffect), ResourceType = typeof(GraphicsStrings))]
public partial class LayerEffect : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        context.CustomEffect(Unit.Default,
            (_, ctx) =>
            {
                var bounds = ctx.Targets.CalculateBounds();
                var newTarget = ctx.CreateTarget(bounds);
                if (newTarget.IsEmpty)
                {
                    newTarget.Dispose();
                    return;
                }

                // ctx.Open bakes the base CTM scale from the target's density.
                using (var canvas = ctx.Open(newTarget))
                {
                    canvas.Clear();
                    foreach (var t in ctx.Targets)
                    {
                        using (canvas.PushTransform(Matrix.CreateTranslation(t.Bounds.Position - bounds.Position)))
                        {
                            t.Draw(canvas);
                        }
                    }
                }

                for (int i = ctx.Targets.Count - 1; i >= 0; i--)
                {
                    ctx.Targets[i].Dispose();
                    ctx.Targets.RemoveAt(i);
                }

                ctx.Targets.Add(newTarget);
            },
            // Flattening the targets into their own union never leaves the incoming extent.
            static (_, bounds) => bounds);
    }
}
