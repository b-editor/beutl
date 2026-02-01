using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

using Image = Silk.NET.Vulkan.Image;
using Semaphore = Silk.NET.Vulkan.Semaphore;

internal sealed unsafe class VulkanCommandPool : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly Queue _graphicsQueue;
    private readonly uint _graphicsQueueFamilyIndex;
    private readonly CommandPool _commandPool;
    private readonly Fence _immediateFence;
    private Semaphore _submissionSemaphore;
    private bool _hasPendingSemaphoreSignal;
    private bool _disposed;

    public VulkanCommandPool(Vk vk, Device device, Queue graphicsQueue, uint graphicsQueueFamilyIndex)
    {
        _vk = vk;
        _device = device;
        _graphicsQueue = graphicsQueue;
        _graphicsQueueFamilyIndex = graphicsQueueFamilyIndex;

        _commandPool = CreateCommandPool();
        _immediateFence = CreateFence();
        _submissionSemaphore = CreateSemaphore();
    }

    public CommandPool CommandPool => _commandPool;

    public Fence ImmediateFence => _immediateFence;

    public Semaphore SubmissionSemaphore => _submissionSemaphore;

    private CommandPool CreateCommandPool()
    {
        var createInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _graphicsQueueFamilyIndex,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit |
                    CommandPoolCreateFlags.TransientBit
        };

        CommandPool pool;
        var result = _vk.CreateCommandPool(_device, &createInfo, null, &pool);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Failed to create command pool: {result}");
        }

        return pool;
    }

    private Fence CreateFence()
    {
        var createInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };

        Fence fence;
        var result = _vk.CreateFence(_device, &createInfo, null, &fence);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Failed to create fence: {result}");
        }

        return fence;
    }

    private Semaphore CreateSemaphore()
    {
        var createInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };

        Semaphore semaphore;
        var result = _vk.CreateSemaphore(_device, &createInfo, null, &semaphore);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Failed to create semaphore: {result}");
        }

        return semaphore;
    }

    public void SubmitImmediateCommands(Action<CommandBuffer> record)
    {
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        CommandBuffer commandBuffer;
        _vk.AllocateCommandBuffers(_device, &allocInfo, &commandBuffer);

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        _vk.BeginCommandBuffer(commandBuffer, &beginInfo);
        record(commandBuffer);
        _vk.EndCommandBuffer(commandBuffer);

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };

        fixed (Semaphore* submissionSemaphore = &_submissionSemaphore)
        fixed (Fence* immediateFence = &_immediateFence)
        {
            PipelineStageFlags waitDstStageMask = PipelineStageFlags.AllCommandsBit;
            if (_hasPendingSemaphoreSignal)
            {
                submitInfo.WaitSemaphoreCount = 1;
                submitInfo.PWaitSemaphores = submissionSemaphore;
                submitInfo.PWaitDstStageMask = &waitDstStageMask;
            }

            submitInfo.SignalSemaphoreCount = 1;
            submitInfo.PSignalSemaphores = submissionSemaphore;

            _vk.ResetFences(_device, 1, immediateFence);
            _vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, _immediateFence);
            _vk.WaitForFences(_device, 1, immediateFence, Vk.True, ulong.MaxValue);

            _hasPendingSemaphoreSignal = true;
            _vk.FreeCommandBuffers(_device, _commandPool, 1, &commandBuffer);
        }
    }

    public CommandBuffer AllocateCommandBuffer()
    {
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        CommandBuffer commandBuffer;
        var result = _vk.AllocateCommandBuffers(_device, &allocInfo, &commandBuffer);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Failed to allocate command buffer: {result}");
        }
        return commandBuffer;
    }

    public void SubmitCommandBuffer(CommandBuffer commandBuffer)
    {
        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };

        fixed (Semaphore* submissionSemaphore = &_submissionSemaphore)
        fixed (Fence* immediateFence = &_immediateFence)
        {
            PipelineStageFlags waitDstStageMask = PipelineStageFlags.AllCommandsBit;
            if (_hasPendingSemaphoreSignal)
            {
                submitInfo.WaitSemaphoreCount = 1;
                submitInfo.PWaitSemaphores = submissionSemaphore;
                submitInfo.PWaitDstStageMask = &waitDstStageMask;
            }

            submitInfo.SignalSemaphoreCount = 1;
            submitInfo.PSignalSemaphores = submissionSemaphore;

            _vk.ResetFences(_device, 1, immediateFence);
            _vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, _immediateFence);
            _vk.WaitForFences(_device, 1, immediateFence, Vk.True, ulong.MaxValue);

            _hasPendingSemaphoreSignal = true;
            _vk.FreeCommandBuffers(_device, _commandPool, 1, &commandBuffer);
        }
    }

    public void TransitionImageLayout(Image image, ImageLayout oldLayout, ImageLayout newLayout)
    {
        TransitionImageLayout(image, oldLayout, newLayout, ImageAspectFlags.ColorBit);
    }

    public void TransitionImageLayout(Image image, ImageLayout oldLayout, ImageLayout newLayout, ImageAspectFlags aspectMask)
    {
        SubmitImmediateCommands(commandBuffer =>
        {
            ImageMemoryBarrier barrier = new()
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = oldLayout,
                NewLayout = newLayout,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = aspectMask,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            GetPipelineStages(oldLayout, newLayout, out PipelineStageFlags srcStage, out PipelineStageFlags dstStage,
                out AccessFlags srcAccess, out AccessFlags dstAccess);

            barrier.SrcAccessMask = srcAccess;
            barrier.DstAccessMask = dstAccess;

            _vk.CmdPipelineBarrier(commandBuffer, srcStage, dstStage, 0, 0, null, 0, null, 1, &barrier);
        });
    }

    public void TransitionImageLayout(
        Image image,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        ImageAspectFlags aspectMask,
        uint baseArrayLayer,
        uint layerCount)
    {
        SubmitImmediateCommands(commandBuffer =>
        {
            ImageMemoryBarrier barrier = new()
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = oldLayout,
                NewLayout = newLayout,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = aspectMask,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = baseArrayLayer,
                    LayerCount = layerCount
                }
            };

            GetPipelineStages(oldLayout, newLayout, out PipelineStageFlags srcStage, out PipelineStageFlags dstStage,
                out AccessFlags srcAccess, out AccessFlags dstAccess);

            barrier.SrcAccessMask = srcAccess;
            barrier.DstAccessMask = dstAccess;

            _vk.CmdPipelineBarrier(commandBuffer, srcStage, dstStage, 0, 0, null, 0, null, 1, &barrier);
        });
    }

    private static void GetPipelineStages(
        ImageLayout oldLayout,
        ImageLayout newLayout,
        out PipelineStageFlags srcStage,
        out PipelineStageFlags dstStage,
        out AccessFlags srcAccess,
        out AccessFlags dstAccess)
    {
        srcStage = PipelineStageFlags.TopOfPipeBit;
        dstStage = PipelineStageFlags.BottomOfPipeBit;
        srcAccess = 0;
        dstAccess = 0;

        if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.ColorAttachmentOptimal)
        {
            srcStage = PipelineStageFlags.TopOfPipeBit;
            dstStage = PipelineStageFlags.ColorAttachmentOutputBit;
            srcAccess = 0;
            dstAccess = AccessFlags.ColorAttachmentWriteBit;
        }
        else if (oldLayout == ImageLayout.ColorAttachmentOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            srcStage = PipelineStageFlags.ColorAttachmentOutputBit;
            dstStage = PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ComputeShaderBit;
            srcAccess = AccessFlags.ColorAttachmentWriteBit;
            dstAccess = AccessFlags.ShaderReadBit;
        }
        else if (oldLayout == ImageLayout.ShaderReadOnlyOptimal && newLayout == ImageLayout.ColorAttachmentOptimal)
        {
            srcStage = PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ComputeShaderBit;
            dstStage = PipelineStageFlags.ColorAttachmentOutputBit;
            srcAccess = AccessFlags.ShaderReadBit;
            dstAccess = AccessFlags.ColorAttachmentWriteBit;
        }
        else if (oldLayout == ImageLayout.ColorAttachmentOptimal && newLayout == ImageLayout.TransferSrcOptimal)
        {
            srcStage = PipelineStageFlags.ColorAttachmentOutputBit;
            dstStage = PipelineStageFlags.TransferBit;
            srcAccess = AccessFlags.ColorAttachmentWriteBit;
            dstAccess = AccessFlags.TransferReadBit;
        }
        else if (oldLayout == ImageLayout.TransferSrcOptimal && newLayout == ImageLayout.ColorAttachmentOptimal)
        {
            srcStage = PipelineStageFlags.TransferBit;
            dstStage = PipelineStageFlags.ColorAttachmentOutputBit;
            srcAccess = AccessFlags.TransferReadBit;
            dstAccess = AccessFlags.ColorAttachmentWriteBit;
        }
        // Depth image transitions
        else if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.DepthStencilAttachmentOptimal)
        {
            srcStage = PipelineStageFlags.TopOfPipeBit;
            dstStage = PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit;
            srcAccess = 0;
            dstAccess = AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit;
        }
        else if (oldLayout == ImageLayout.DepthStencilAttachmentOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            srcStage = PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit;
            dstStage = PipelineStageFlags.FragmentShaderBit;
            srcAccess = AccessFlags.DepthStencilAttachmentWriteBit;
            dstAccess = AccessFlags.ShaderReadBit;
        }
        else if (oldLayout == ImageLayout.ShaderReadOnlyOptimal && newLayout == ImageLayout.DepthStencilAttachmentOptimal)
        {
            srcStage = PipelineStageFlags.FragmentShaderBit;
            dstStage = PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit;
            srcAccess = AccessFlags.ShaderReadBit;
            dstAccess = AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit;
        }
        // Transfer transitions for texture upload
        else if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.TransferDstOptimal)
        {
            srcStage = PipelineStageFlags.TopOfPipeBit;
            dstStage = PipelineStageFlags.TransferBit;
            srcAccess = 0;
            dstAccess = AccessFlags.TransferWriteBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            srcStage = PipelineStageFlags.TransferBit;
            dstStage = PipelineStageFlags.FragmentShaderBit;
            srcAccess = AccessFlags.TransferWriteBit;
            dstAccess = AccessFlags.ShaderReadBit;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_immediateFence.Handle != 0)
        {
            _vk.DestroyFence(_device, _immediateFence, null);
        }

        if (_commandPool.Handle != 0)
        {
            _vk.DestroyCommandPool(_device, _commandPool, null);
        }

        if (_submissionSemaphore.Handle != 0)
        {
            _vk.DestroySemaphore(_device, _submissionSemaphore, null);
        }
    }
}
