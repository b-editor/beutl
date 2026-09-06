namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// A render pass instance that can be ended and begun again so work Vulkan forbids inside it can be
/// recorded where it was asked for.
/// </summary>
/// <remarks>
/// Without this, a transfer or barrier requested mid-pass has to take its own batch, and that batch submits
/// ahead of the pass still being recorded - so a draw already recorded in the pass runs after work requested
/// later than it. Splitting the pass keeps the whole sequence on one command buffer in recording order.
/// </remarks>
internal interface IVulkanRenderPassSuspension
{
    /// <summary>Ends the open pass instance, if one is open.</summary>
    /// <returns>Whether an instance was open and has been ended, so it must be resumed.</returns>
    bool TrySuspend();

    /// <summary>Begins the instance again, loading what the suspended half stored.</summary>
    void Resume();
}
