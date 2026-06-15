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
                // feature 003: ctx.Open bakes the base CTM scale from the target's density (so an FR-037(b)
                // buffer-budget clamp is honored), mapping logical child placement onto the device buffer
                // automatically. No manual prescale; density 1 stays byte-identical.
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
            });
    }
}
