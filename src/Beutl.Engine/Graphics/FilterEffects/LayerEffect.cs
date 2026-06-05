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
                // feature 003: the buffer is ceil(bounds × w) DEVICE px while the child placement is LOGICAL and
                // t.Draw maps an At(w) child into its logical rect, so prescale the canvas by w to map the logical
                // composite onto the full device buffer. w == 1 keeps the bare identity path (byte-identical).
                float w = ctx.WorkingScale;
                using (var canvas = ctx.Open(newTarget))
                using (w == 1f ? default : canvas.PushTransform(Matrix.CreateScale(w, w)))
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
