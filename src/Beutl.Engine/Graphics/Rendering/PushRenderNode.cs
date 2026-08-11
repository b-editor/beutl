namespace Beutl.Graphics.Rendering;

public sealed class PushRenderNode : ContainerRenderNode
{
    private static readonly TargetScopeDefinition<PushState> s_definition =
        TargetScopeDefinition<PushState>.Create(
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
            deviceGridMapping: RenderDeviceGridMapping.Preserved);

    public override void Process(RenderNodeContext context)
    {
        context.PublishMappedInputs(
            s_definition.Call(default),
            static (context, input, value) => context.TargetScope(input, value));
    }

    private readonly record struct PushState;
}
