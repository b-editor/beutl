using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

using Image = Silk.NET.Vulkan.Image;
using Semaphore = Silk.NET.Vulkan.Semaphore;

internal sealed unsafe class VulkanCommandPool : IDisposable
{
    private static readonly AsyncLocal<ObservationScope?> s_observer = new();
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly Queue _graphicsQueue;
    private readonly uint _graphicsQueueFamilyIndex;
    private readonly CommandPool _commandPool;
    private readonly List<InFlightSubmission> _inFlightSubmissions = [];
    private readonly List<Action> _recordingReleases = [];
    private CommandBuffer _recordingCommandBuffer;
    private Semaphore _submissionSemaphore;
    private bool _isRecording;
    private int _renderPassScopeDepth;
    private object? _activeRenderPassOwner;
    private bool _hasPendingSemaphoreSignal;
    private bool _isCompletingSubmissions;
    private bool _disposed;

    public VulkanCommandPool(Vk vk, Device device, Queue graphicsQueue, uint graphicsQueueFamilyIndex)
    {
        _vk = vk;
        _device = device;
        _graphicsQueue = graphicsQueue;
        _graphicsQueueFamilyIndex = graphicsQueueFamilyIndex;

        _commandPool = CreateCommandPool();
    }

    internal static IDisposable Observe(Action<VulkanCommandPoolEvent> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var scope = new ObservationScope(observer, s_observer.Value);
        s_observer.Value = scope;
        return scope;
    }

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

    public void RecordCommands(Action<CommandBuffer> record)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(record);
        if (_renderPassScopeDepth > 0)
        {
            RecordOutOfBand(record);
            return;
        }

        record(GetRecordingCommandBuffer());
    }

    /// <summary>Rejects a caller that is about to record a render pass while another one owns the batch.</summary>
    /// <remarks>
    /// Separate from <see cref="BeginRenderPassScope"/> so a pass can reject a double begin before it
    /// records anything, while still claiming the batch only at the command that opens the pass: claiming
    /// it earlier would divert the pass's own preparation barriers into an out-of-band batch that submits
    /// ahead of everything already recorded, which reorders them against work they must follow.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Another render pass already owns the batch.</exception>
    public void ThrowIfRenderPassActive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activeRenderPassOwner is not null)
        {
            throw new InvalidOperationException(
                "A render pass instance is already recording on this context's command buffer. Vulkan does "
                + "not allow one render pass inside another, so the active pass must end before the next "
                + "begins.");
        }
    }

    /// <summary>
    /// Claims the recording batch for one render pass instance, during which transfers and barriers cannot
    /// join it.
    /// </summary>
    /// <param name="owner">The render pass claiming the batch.</param>
    /// <remarks>
    /// Every pass on this context records into one shared command buffer, and Vulkan forbids a render pass
    /// instance inside another on the same buffer. Ownership is exclusive rather than counted so a second
    /// pass is rejected here instead of reaching <c>vkCmdBeginRenderPass</c>, where it would invalidate the
    /// buffer the first pass is still recording into.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Another render pass already owns the batch.</exception>
    public void BeginRenderPassScope(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ThrowIfRenderPassActive();
        _activeRenderPassOwner = owner;
        _renderPassScopeDepth++;
    }

    /// <summary>Releases the recording batch claimed by <paramref name="owner"/>.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="owner"/> does not own the batch.</exception>
    public void EndRenderPassScope(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!ReferenceEquals(_activeRenderPassOwner, owner))
        {
            throw new InvalidOperationException(
                "Only the render pass that claimed this context's command buffer can release it.");
        }

        _activeRenderPassOwner = null;
        if (_renderPassScopeDepth > 0)
            _renderPassScopeDepth--;
    }

    // Vulkan forbids transfers and barriers inside a render pass instance, so one recorded there
    // takes its own batch, submitted ahead of the pass that is still recording. The submission
    // semaphore orders it before that pass without stalling the CPU.
    private void RecordOutOfBand(Action<CommandBuffer> record)
    {
        CommandBuffer suspended = _recordingCommandBuffer;
        bool wasRecording = _isRecording;
        Action[] suspendedReleases = [.. _recordingReleases];
        _recordingCommandBuffer = default;
        _isRecording = false;
        _recordingReleases.Clear();
        int scopeDepth = _renderPassScopeDepth;
        _renderPassScopeDepth = 0;
        try
        {
            record(GetRecordingCommandBuffer());
            SubmitRecordingCommandBuffer();
        }
        finally
        {
            _renderPassScopeDepth = scopeDepth;
            _recordingCommandBuffer = suspended;
            _isRecording = wasRecording;
            _recordingReleases.Clear();
            _recordingReleases.AddRange(suspendedReleases);
        }
    }

    /// <summary>
    /// Records, submits, and waits for an isolated one-shot command buffer without consuming the
    /// open recording batch or retiring its deferred releases.
    /// </summary>
    /// <remarks>
    /// This is reserved for Vulkan callbacks whose caller can use the affected resource as soon as
    /// the callback returns. Queue order places the isolated submission after previously submitted
    /// work and before the still-open recording batch when that batch is eventually submitted.
    /// </remarks>
    public void SubmitIsolatedCommands(Action<CommandBuffer> record)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(record);

        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };

        CommandBuffer commandBuffer = default;
        Fence fence = default;
        bool commandBufferAllocated = false;
        try
        {
            Result result = _vk.AllocateCommandBuffers(_device, &allocInfo, &commandBuffer);
            if (result != Result.Success)
            {
                throw new InvalidOperationException(
                    $"Failed to allocate an isolated command buffer: {result}");
            }
            commandBufferAllocated = true;

            var beginInfo = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };
            result = _vk.BeginCommandBuffer(commandBuffer, &beginInfo);
            if (result != Result.Success)
            {
                throw new InvalidOperationException(
                    $"Failed to begin an isolated command buffer: {result}");
            }

            record(commandBuffer);

            result = _vk.EndCommandBuffer(commandBuffer);
            if (result != Result.Success)
            {
                throw new InvalidOperationException(
                    $"Failed to end an isolated command buffer: {result}");
            }

            fence = CreateFence();
            var submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &commandBuffer,
            };
            result = _vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, fence);
            if (result != Result.Success)
            {
                throw new InvalidOperationException(
                    $"Failed to submit an isolated command buffer: {result}");
            }
            RecordEvent(VulkanCommandPoolEvent.Submission);

            result = _vk.WaitForFences(_device, 1, &fence, Vk.True, ulong.MaxValue);
            if (result != Result.Success)
            {
                throw new InvalidOperationException(
                    $"Failed to wait for an isolated command buffer: {result}");
            }
            RecordEvent(VulkanCommandPoolEvent.FenceWait);
        }
        finally
        {
            if (fence.Handle != 0)
            {
                _vk.DestroyFence(_device, fence, null);
            }
            if (commandBufferAllocated)
            {
                _vk.FreeCommandBuffers(_device, _commandPool, 1, &commandBuffer);
            }
        }
    }

    public CommandBuffer GetRecordingCommandBuffer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CollectCompletedSubmissions();

        if (_isRecording)
            return _recordingCommandBuffer;

        var allocInfo = new CommandBufferAllocateInfo
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
        _recordingCommandBuffer = commandBuffer;

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        result = _vk.BeginCommandBuffer(_recordingCommandBuffer, &beginInfo);
        if (result != Result.Success)
        {
            _vk.FreeCommandBuffers(_device, _commandPool, 1, &commandBuffer);
            _recordingCommandBuffer = default;
            throw new InvalidOperationException($"Failed to begin command buffer: {result}");
        }

        _isRecording = true;
        return _recordingCommandBuffer;
    }

    public void Flush(bool waitForCompletion)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // A render pass instance owns the recording batch until it ends, so submitting it here would
        // end and free the command buffer the pass is still recording into. Everything recorded during
        // the scope already took its own batch through RecordOutOfBand, so waiting on the in-flight
        // submissions is the whole of what a synchronous caller can be owed.
        if (_renderPassScopeDepth == 0)
        {
            SubmitRecordingCommandBuffer();
        }

        if (waitForCompletion)
        {
            WaitForInFlightSubmissions();
        }
        else
        {
            CollectCompletedSubmissions();
        }
    }

    public void DeferRelease(Action release)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (_disposed)
        {
            release();
            return;
        }

        CollectCompletedSubmissions();
        if (_isRecording)
        {
            _recordingReleases.Add(release);
        }
        else if (_inFlightSubmissions.Count > 0)
        {
            _inFlightSubmissions[^1].Releases.Add(release);
        }
        else
        {
            release();
        }
    }

    private void SubmitRecordingCommandBuffer()
    {
        if (!_isRecording)
            return;

        CommandBuffer commandBuffer = _recordingCommandBuffer;
        Action[] releases = [.. _recordingReleases];
        _recordingCommandBuffer = default;
        _recordingReleases.Clear();
        _isRecording = false;

        Fence fence = default;
        Semaphore signalSemaphore = default;
        Semaphore waitSemaphore = _submissionSemaphore;
        InFlightSubmission? submission = null;
        try
        {
            Result result = _vk.EndCommandBuffer(commandBuffer);
            if (result != Result.Success)
            {
                throw new InvalidOperationException($"Failed to end command buffer: {result}");
            }

            fence = CreateFence();
            signalSemaphore = CreateSemaphore();
            PipelineStageFlags waitDstStageMask = PipelineStageFlags.AllCommandsBit;
            var submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &commandBuffer,
                SignalSemaphoreCount = 1,
                PSignalSemaphores = &signalSemaphore
            };

            if (_hasPendingSemaphoreSignal)
            {
                submitInfo.WaitSemaphoreCount = 1;
                submitInfo.PWaitSemaphores = &waitSemaphore;
                submitInfo.PWaitDstStageMask = &waitDstStageMask;
            }

            submission = new InFlightSubmission(commandBuffer, fence);
            if (_hasPendingSemaphoreSignal)
            {
                submission.WaitSemaphores.Add(waitSemaphore);
            }
            submission.Releases.AddRange(releases);
            _inFlightSubmissions.EnsureCapacity(_inFlightSubmissions.Count + 1);

            result = _vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, fence);
            if (result != Result.Success)
            {
                throw new InvalidOperationException($"Failed to submit command buffer: {result}");
            }
        }
        catch (Exception submitException)
        {
            if (signalSemaphore.Handle != 0)
            {
                _vk.DestroySemaphore(_device, signalSemaphore, null);
            }
            if (fence.Handle != 0)
            {
                _vk.DestroyFence(_device, fence, null);
            }
            _vk.FreeCommandBuffers(_device, _commandPool, 1, &commandBuffer);

            try
            {
                RetireUnsubmittedReleases(releases);
            }
            catch (Exception releaseException)
            {
                throw new AggregateException(
                    "Vulkan submission and deferred-resource retirement both failed.",
                    submitException,
                    releaseException);
            }

            throw;
        }

        _inFlightSubmissions.Add(submission!);
        _submissionSemaphore = signalSemaphore;
        _hasPendingSemaphoreSignal = true;
        RecordEvent(VulkanCommandPoolEvent.Submission);
    }

    private void RetireUnsubmittedReleases(Action[] releases)
    {
        // A resource referenced by the failed recording may also be referenced by an older
        // submission. Keep its release behind that submission instead of freeing it immediately.
        if (_inFlightSubmissions.Count > 0)
        {
            _inFlightSubmissions[^1].Releases.AddRange(releases);
        }
        else
        {
            InvokeReleases(releases);
        }
    }

    private void WaitForInFlightSubmissions()
    {
        if (_inFlightSubmissions.Count == 0)
            return;

        var fences = new Fence[_inFlightSubmissions.Count];
        for (int i = 0; i < fences.Length; i++)
        {
            fences[i] = _inFlightSubmissions[i].Fence;
        }

        fixed (Fence* pFences = fences)
        {
            Result result = _vk.WaitForFences(_device, (uint)fences.Length, pFences, Vk.True, ulong.MaxValue);
            if (result != Result.Success)
            {
                throw new InvalidOperationException($"Failed to wait for Vulkan submissions: {result}");
            }
        }

        RecordEvent(VulkanCommandPoolEvent.FenceWait);
        while (_inFlightSubmissions.Count > 0)
        {
            CompleteSubmission(0);
        }
        ResetSubmissionSemaphore();
    }

    private void CollectCompletedSubmissions()
    {
        if (_isCompletingSubmissions)
            return;

        while (_inFlightSubmissions.Count > 0
               && _vk.GetFenceStatus(_device, _inFlightSubmissions[0].Fence) == Result.Success)
        {
            CompleteSubmission(0);
        }

        if (_inFlightSubmissions.Count == 0)
        {
            ResetSubmissionSemaphore();
        }
    }

    private void CompleteSubmission(int index)
    {
        InFlightSubmission submission = _inFlightSubmissions[index];
        _inFlightSubmissions.RemoveAt(index);
        Action[] releases = [.. submission.Releases];
        submission.Releases.Clear();

        CommandBuffer commandBuffer = submission.CommandBuffer;
        _vk.FreeCommandBuffers(_device, _commandPool, 1, &commandBuffer);
        _vk.DestroyFence(_device, submission.Fence, null);
        foreach (Semaphore semaphore in submission.WaitSemaphores)
        {
            _vk.DestroySemaphore(_device, semaphore, null);
        }

        bool wasCompletingSubmissions = _isCompletingSubmissions;
        _isCompletingSubmissions = true;
        try
        {
            InvokeReleases(releases);
        }
        finally
        {
            _isCompletingSubmissions = wasCompletingSubmissions;
        }
    }

    private static void InvokeReleases(IEnumerable<Action> releases)
    {
        List<Exception>? exceptions = null;
        foreach (Action release in releases)
        {
            try
            {
                release();
            }
            catch (Exception exception)
            {
                (exceptions ??= []).Add(exception);
            }
        }

        if (exceptions is not null)
        {
            throw new AggregateException("One or more deferred Vulkan resource releases failed.", exceptions);
        }
    }

    private void ResetSubmissionSemaphore()
    {
        if (_hasPendingSemaphoreSignal)
        {
            _vk.DestroySemaphore(_device, _submissionSemaphore, null);
            _submissionSemaphore = default;
            _hasPendingSemaphoreSignal = false;
        }
    }

    public void TransitionImageLayout(Image image, ImageLayout oldLayout, ImageLayout newLayout)
    {
        TransitionImageLayout(image, oldLayout, newLayout, ImageAspectFlags.ColorBit);
    }

    public void TransitionImageLayout(Image image, ImageLayout oldLayout, ImageLayout newLayout, ImageAspectFlags aspectMask)
    {
        RecordCommands(commandBuffer =>
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
        RecordCommands(commandBuffer =>
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
        (srcStage, srcAccess) = oldLayout switch
        {
            ImageLayout.Undefined => (PipelineStageFlags.TopOfPipeBit, (AccessFlags)0),
            ImageLayout.ColorAttachmentOptimal => (
                PipelineStageFlags.ColorAttachmentOutputBit,
                AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit),
            ImageLayout.DepthStencilAttachmentOptimal => (
                PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit),
            ImageLayout.ShaderReadOnlyOptimal => (
                PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ComputeShaderBit,
                AccessFlags.ShaderReadBit),
            ImageLayout.TransferSrcOptimal => (PipelineStageFlags.TransferBit, AccessFlags.TransferReadBit),
            ImageLayout.TransferDstOptimal => (PipelineStageFlags.TransferBit, AccessFlags.TransferWriteBit),
            _ => (PipelineStageFlags.AllCommandsBit, AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit),
        };

        (dstStage, dstAccess) = newLayout switch
        {
            ImageLayout.ColorAttachmentOptimal => (
                PipelineStageFlags.ColorAttachmentOutputBit,
                AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit),
            ImageLayout.DepthStencilAttachmentOptimal => (
                PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
                AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit),
            ImageLayout.ShaderReadOnlyOptimal => (
                PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ComputeShaderBit,
                AccessFlags.ShaderReadBit),
            ImageLayout.TransferSrcOptimal => (PipelineStageFlags.TransferBit, AccessFlags.TransferReadBit),
            ImageLayout.TransferDstOptimal => (PipelineStageFlags.TransferBit, AccessFlags.TransferWriteBit),
            _ => (PipelineStageFlags.AllCommandsBit, AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit),
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            Flush(waitForCompletion: true);
        }
        catch (Exception flushException)
        {
            try
            {
                // Submission failure leaves the recording detached, but older submissions may
                // still own resources retired from it. Complete those before destroying the pool.
                WaitForInFlightSubmissions();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "Vulkan command-pool flush and cleanup both failed.",
                    flushException,
                    cleanupException);
            }

            throw;
        }
        finally
        {
            _disposed = true;
            try
            {
                // A batch left recording means a render pass never ended, so the flush above withheld it.
                // The pool is going away, so reclaim the buffer and retire the releases a submission
                // would have run rather than dropping them.
                ReclaimUnsubmittedRecording();
            }
            finally
            {
                try
                {
                    if (_inFlightSubmissions.Count == 0)
                    {
                        ResetSubmissionSemaphore();
                    }
                }
                finally
                {
                    if (_commandPool.Handle != 0)
                    {
                        _vk.DestroyCommandPool(_device, _commandPool, null);
                    }
                }
            }
        }
    }

    private void ReclaimUnsubmittedRecording()
    {
        if (!_isRecording)
            return;

        CommandBuffer commandBuffer = _recordingCommandBuffer;
        Action[] releases = [.. _recordingReleases];
        _recordingCommandBuffer = default;
        _recordingReleases.Clear();
        _isRecording = false;
        _renderPassScopeDepth = 0;
        _activeRenderPassOwner = null;

        _vk.FreeCommandBuffers(_device, _commandPool, 1, &commandBuffer);
        InvokeReleases(releases);
    }

    private static void RecordEvent(VulkanCommandPoolEvent eventType)
    {
        for (ObservationScope? scope = s_observer.Value; scope is not null; scope = scope.Parent)
        {
            try
            {
                scope.Observer(eventType);
            }
            catch
            {
                // Diagnostics must never affect rendering or cleanup.
            }
        }
    }

    private sealed class InFlightSubmission(CommandBuffer commandBuffer, Fence fence)
    {
        public CommandBuffer CommandBuffer = commandBuffer;

        public Fence Fence { get; } = fence;

        public List<Semaphore> WaitSemaphores { get; } = [];

        public List<Action> Releases { get; } = [];
    }

    private sealed class ObservationScope(
        Action<VulkanCommandPoolEvent> observer,
        ObservationScope? parent) : IDisposable
    {
        private bool _disposed;

        public Action<VulkanCommandPoolEvent> Observer { get; } = observer;

        public ObservationScope? Parent { get; } = parent;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (ReferenceEquals(s_observer.Value, this))
            {
                s_observer.Value = Parent;
            }
        }
    }
}

internal enum VulkanCommandPoolEvent : byte
{
    Submission,
    FenceWait,
}
