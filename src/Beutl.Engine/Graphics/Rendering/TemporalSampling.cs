using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

// Each slot owns its evaluated resources until the next frame. Render nodes may retain those resources;
// mutating one resource repeatedly while recording samples would make every sample draw the last time.
internal sealed class TemporalSampling : IDisposable
{
    internal sealed class Sample(CompositionEvaluationSession session) : IDisposable
    {
        public CompositionEvaluationSession Session { get; } = session;
        public List<Drawable.Resource> Drawables { get; } = [];

        public void Dispose()
        {
            foreach (Drawable.Resource drawable in Drawables) drawable.Dispose();
            Drawables.Clear();
            Session.Dispose();
        }
    }

    public List<Sample> Samples { get; } = [];

    public bool Reconcile(CompositionContext context, IReadOnlyList<Drawable.Resource> inputs,
        float shutterAngle, ShutterPosition position, int sampleCount)
    {
        double angle = float.IsFinite(shutterAngle) ? Math.Clamp(shutterAngle, 0, 360) : 0;
        int count = angle == 0 ? 1 : Math.Clamp(sampleCount, 1, 64);
        double rate = context.FrameRate.ToDouble();
        double exposure = angle / 360 / (double.IsFinite(rate) && rate > 0 ? rate : 30);
        bool changed = Samples.Count != count;
        while (Samples.Count > count)
        {
            Samples[^1].Dispose();
            Samples.RemoveAt(Samples.Count - 1);
        }

        for (int i = 0; i < count; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (i == Samples.Count) Samples.Add(new Sample(context.CreateEvaluationSession()));
            Sample sample = Samples[i];
            double reference = position switch { ShutterPosition.Start => 0, ShutterPosition.End => 1, _ => 0.5 };
            MediaTime time = count == 1 ? context.Time
                : context.Time + MediaTime.FromSeconds(((i + 0.5) / count - reference) * exposure);
            sample.Session.Update(context, time);

            int index = 0;
            foreach (Drawable.Resource input in inputs)
            {
                if (FlowNode.Capture(input) is not { } node) continue;
                TimeRange range = node.Object.TimeRange.Value;
                if (range.Duration > MediaTime.Zero && !range.Contains(time)) continue;
                Drawable.Resource? resource = index < sample.Drawables.Count ? sample.Drawables[index] : null;
                node.Reconcile(sample.Session.Context, ref resource, ref changed);
                if (index < sample.Drawables.Count) sample.Drawables[index] = resource!;
                else sample.Drawables.Add(resource!);
                index++;
            }

            while (sample.Drawables.Count > index)
            {
                sample.Drawables[^1].Dispose();
                sample.Drawables.RemoveAt(sample.Drawables.Count - 1);
                changed = true;
            }
            context.ContainsScene3D |= sample.Session.Context.ContainsScene3D;
        }
        return changed;
    }

    public void Draw(GraphicsContext2D context, CancellationToken cancellationToken)
    {
        using (PushIsolation(context))
        {
            for (int i = 0; i < Samples.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (context.PushBlendMode(i == 0 ? BlendMode.SrcOver : BlendMode.Plus))
                using (context.PushOpacity(1f / Samples.Count))
                using (PushIsolation(context))
                {
                    foreach (Drawable.Resource drawable in Samples[i].Drawables)
                        context.DrawDrawable(drawable);
                }
            }
        }
    }

    private static PushedState PushIsolation(GraphicsContext2D context) => context.PushNode(
        true,
        static value => new DrawableGroup.ContentIsolationRenderNode(value),
        static (node, value) => node.Update(value));

    public void Dispose()
    {
        foreach (Sample sample in Samples) sample.Dispose();
        Samples.Clear();
    }
}
