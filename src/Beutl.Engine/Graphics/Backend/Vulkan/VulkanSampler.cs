using System;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

internal sealed unsafe class VulkanSampler : ISampler, IVulkanContextResource
{
    private readonly VulkanContext _context;
    private readonly Sampler _sampler;
    private bool _disposed;

    public VulkanContext OwnerContext => _context;

    public VulkanSampler(
        VulkanContext context,
        SamplerFilter minFilter = SamplerFilter.Linear,
        SamplerFilter magFilter = SamplerFilter.Linear,
        SamplerAddressMode addressModeU = SamplerAddressMode.ClampToEdge,
        SamplerAddressMode addressModeV = SamplerAddressMode.ClampToEdge,
        bool transparentBorder = false, SamplerAddressMode? addressModeW = null,
        SamplerFilter mipmapFilter = SamplerFilter.Linear, float minLod = 0, float maxLod = 0,
        CompareFunction? compare = null, bool integerBorder = false)
    {
        _context = context;
        MinFilter = minFilter;
        MagFilter = magFilter;
        AddressModeU = addressModeU;
        AddressModeV = addressModeV;

        var vk = context.Vk;
        var device = context.Device;

        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MinFilter = ToVulkanFilter(minFilter),
            MagFilter = ToVulkanFilter(magFilter),
            AddressModeU = ToVulkanAddressMode(addressModeU),
            AddressModeV = ToVulkanAddressMode(addressModeV),
            AddressModeW = ToVulkanAddressMode(addressModeW ?? addressModeU),
            AnisotropyEnable = false,
            MaxAnisotropy = 1.0f,
            BorderColor = integerBorder
                ? (transparentBorder ? BorderColor.IntTransparentBlack : BorderColor.IntOpaqueBlack)
                : (transparentBorder ? BorderColor.FloatTransparentBlack : BorderColor.FloatOpaqueBlack),
            UnnormalizedCoordinates = false,
            CompareEnable = compare.HasValue,
            CompareOp = VulkanFlagConverter.ToVulkan(compare ?? CompareFunction.Always),
            MipmapMode = mipmapFilter == SamplerFilter.Linear ? SamplerMipmapMode.Linear : SamplerMipmapMode.Nearest,
            MipLodBias = 0.0f,
            MinLod = minLod,
            MaxLod = maxLod
        };

        Sampler sampler;
        var result = vk.CreateSampler(device, &samplerInfo, null, &sampler);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Failed to create Vulkan sampler: {result}");
        }
        _sampler = sampler;
    }

    public Sampler Handle => _sampler;

    public SamplerFilter MinFilter { get; }

    public SamplerFilter MagFilter { get; }

    public SamplerAddressMode AddressModeU { get; }

    public SamplerAddressMode AddressModeV { get; }

    private static Filter ToVulkanFilter(SamplerFilter filter) => filter switch
    {
        SamplerFilter.Nearest => Filter.Nearest,
        SamplerFilter.Linear => Filter.Linear,
        _ => Filter.Linear
    };

    private static Silk.NET.Vulkan.SamplerAddressMode ToVulkanAddressMode(SamplerAddressMode mode) => mode switch
    {
        SamplerAddressMode.Repeat => Silk.NET.Vulkan.SamplerAddressMode.Repeat,
        SamplerAddressMode.MirroredRepeat => Silk.NET.Vulkan.SamplerAddressMode.MirroredRepeat,
        SamplerAddressMode.ClampToEdge => Silk.NET.Vulkan.SamplerAddressMode.ClampToEdge,
        SamplerAddressMode.ClampToBorder => Silk.NET.Vulkan.SamplerAddressMode.ClampToBorder,
        _ => Silk.NET.Vulkan.SamplerAddressMode.ClampToEdge
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Silk.NET.Vulkan.Sampler sampler = _sampler;
        _context.DeferRelease(() => _context.Vk.DestroySampler(_context.Device, sampler, null));
    }
}
