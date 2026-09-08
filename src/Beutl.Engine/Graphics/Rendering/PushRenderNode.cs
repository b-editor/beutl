namespace Beutl.Graphics.Rendering;

public sealed class PushRenderNode : ContainerRenderNode
{
    public override void Process(RenderNodeContext context)
    {
        context.PublishMappedInputs(
            TargetScopeDescription.Create(
                default(PushState),
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
                deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive,
                deviceGridMapping: RenderDeviceGridMapping.Preserved),
            static (context, input, value) => context.TargetScope(input, value));
    }

    private readonly record struct PushState;
}
