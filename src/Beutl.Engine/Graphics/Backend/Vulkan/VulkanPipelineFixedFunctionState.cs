using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// The depth, culling and blend state a <see cref="VulkanPipeline3D"/> is created with, in Vulkan terms.
/// </summary>
internal readonly record struct VulkanPipelineFixedFunctionState(
    bool DepthTestEnabled,
    bool DepthWriteEnabled,
    CullModeFlags CullMode,
    Silk.NET.Vulkan.FrontFace FrontFace,
    bool BlendEnabled,
    Silk.NET.Vulkan.BlendFactor SrcColorBlendFactor,
    Silk.NET.Vulkan.BlendFactor DstColorBlendFactor,
    Silk.NET.Vulkan.BlendFactor SrcAlphaBlendFactor,
    Silk.NET.Vulkan.BlendFactor DstAlphaBlendFactor,
    Silk.NET.Vulkan.BlendOp ColorBlendOp,
    Silk.NET.Vulkan.BlendOp AlphaBlendOp)
{
    /// <summary>Converts the backend-neutral options a pipeline was requested with.</summary>
    public static VulkanPipelineFixedFunctionState From(PipelineOptions options) => new(
        options.DepthTestEnabled,
        options.DepthWriteEnabled,
        VulkanFlagConverter.ToVulkan(options.CullMode),
        VulkanFlagConverter.ToVulkan(options.FrontFace),
        options.BlendEnabled,
        VulkanFlagConverter.ToVulkan(options.SrcColorBlendFactor),
        VulkanFlagConverter.ToVulkan(options.DstColorBlendFactor),
        VulkanFlagConverter.ToVulkan(options.SrcAlphaBlendFactor),
        VulkanFlagConverter.ToVulkan(options.DstAlphaBlendFactor),
        VulkanFlagConverter.ToVulkan(options.ColorBlendOp),
        VulkanFlagConverter.ToVulkan(options.AlphaBlendOp));
}
