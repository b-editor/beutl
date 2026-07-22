namespace Beutl.Graphics.Rendering;

public sealed class PushRenderNode : ContainerRenderNode
{
    public override void Process(RenderNodeContext context)
    {
        TargetScopeDescription description = TargetScopeDescription.Create(
            session => session.Canvas.Use(canvas =>
            {
                using (canvas.Push())
                {
                    session.ReplayInput();
                }
            }),
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            RenderScaleContract.PreserveInputSupply,
            structuralKey: typeof(PushRenderNode),
            runtimeIdentity: new RenderRuntimeIdentity(typeof(PushRenderNode)));

        foreach (RenderFragmentHandle input in context.Inputs)
        {
            context.Publish(context.TargetScope(input, description));
        }
    }
}
