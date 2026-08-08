namespace Beutl.Graphics.Rendering;

public sealed class PushRenderNode : ContainerRenderNode
{
    public override void Process(RenderNodeContext context)
    {
        TargetScopeDescription description = TargetScopeDescription.Create(
            typeof(PushRenderNode),
            static (session, _) => session.Canvas.Use(canvas =>
            {
                using (canvas.Push())
                {
                    session.ReplayInput();
                }
            }),
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            RenderScaleContract.PreserveInputSupply,
            RenderDeviceGridMapping.Preserved);

        foreach (RenderFragmentHandle input in context.Inputs)
        {
            context.Publish(context.TargetScope(input, description));
        }
    }
}
