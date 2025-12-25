using System.Reactive;

namespace Beutl.Graphics.Effects;

public partial class LayerEffect : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        context.CustomEffect(Unit.Default,
            (_, ctx) =>
            {
                var bounds = ctx.Targets.CalculateBounds();
                var newTarget = ctx.CreateTarget(bounds);
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
