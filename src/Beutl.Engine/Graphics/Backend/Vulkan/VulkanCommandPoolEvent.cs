namespace Beutl.Graphics.Backend.Vulkan;

internal enum VulkanCommandPoolEvent : byte
{
    Submission,
    FenceWait,
}
