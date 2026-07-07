namespace Beutl.Graphics.Effects;

/// <summary>
/// Shared render for geometry effects that redraw their single input under a per-operation logical matrix pivoting in
/// the input's own space (feature 004, research D7 — the migration home for <c>ShakeEffect</c>,
/// <c>PathFollowEffect</c> and <c>TransformEffect</c>'s <c>ApplyToTarget</c> path). The executor opens the session's
/// canvas at the output buffer density and clears it; this composites the input transformed by
/// <paramref name="localMatrix"/> in logical space, reproducing the legacy custom-effect redraw. The output buffer is
/// sized by the node's forward bounds contract, so the placement offset lines the input up with the mapped rect.
/// </summary>
internal static class TransformGeometry
{
    public static void Render(GeometrySession session, Matrix localMatrix)
    {
        EffectInput input = session.Inputs[0];
        ImmediateCanvas canvas = session.OpenCanvas();
        using (canvas.PushTransform(Matrix.CreateTranslation(input.Bounds.Position - session.Bounds.Position)))
        using (canvas.PushTransform(localMatrix))
        {
            input.Draw(canvas);
        }
    }
}
