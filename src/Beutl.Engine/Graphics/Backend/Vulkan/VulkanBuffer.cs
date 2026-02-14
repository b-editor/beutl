using System;
using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan implementation of <see cref="IBuffer"/>.
/// </summary>
internal sealed unsafe class VulkanBuffer : IBuffer
{
    private readonly VulkanContext _context;
    private readonly Buffer _buffer;
    private readonly DeviceMemory _memory;
    private readonly ulong _size;
    private readonly BufferUsage _usage;
    private readonly MemoryProperty _memoryProperties;
    private bool _disposed;

    public VulkanBuffer(
        VulkanContext context,
        ulong size,
        BufferUsage usage,
        MemoryProperty memoryProperties)
    {
        _context = context;
        _size = size;
        _usage = usage;
        _memoryProperties = memoryProperties;

        var vk = context.Vk;
        var device = context.Device;

        var vulkanUsage = VulkanFlagConverter.ToVulkan(usage);
        var vulkanMemoryProperties = VulkanFlagConverter.ToVulkan(memoryProperties);

        // Create buffer
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = vulkanUsage,
            SharingMode = SharingMode.Exclusive
        };

        Buffer buffer;
        var result = vk.CreateBuffer(device, &bufferInfo, null, &buffer);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Failed to create Vulkan buffer: {result}");
        }
        _buffer = buffer;

        // Get memory requirements
        MemoryRequirements memReqs;
        vk.GetBufferMemoryRequirements(device, _buffer, &memReqs);

        // Allocate memory
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = context.FindMemoryType(memReqs.MemoryTypeBits, vulkanMemoryProperties)
        };

        DeviceMemory memory;
        result = vk.AllocateMemory(device, &allocInfo, null, &memory);
        if (result != Result.Success)
        {
            vk.DestroyBuffer(device, _buffer, null);
            throw new InvalidOperationException($"Failed to allocate Vulkan buffer memory: {result}");
        }
        _memory = memory;

        // Bind memory to buffer
        result = vk.BindBufferMemory(device, _buffer, _memory, 0);
        if (result != Result.Success)
        {
            vk.FreeMemory(device, _memory, null);
            vk.DestroyBuffer(device, _buffer, null);
            throw new InvalidOperationException($"Failed to bind buffer memory: {result}");
        }
    }

    public ulong Size => _size;

    public Buffer Handle => _buffer;

    public void Upload<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var dataSize = (ulong)(data.Length * Unsafe.SizeOf<T>());
        if (dataSize > _size)
        {
            throw new ArgumentException($"Data size ({dataSize}) exceeds buffer size ({_size})");
        }

        var ptr = Map();
        try
        {
            fixed (T* srcPtr = data)
            {
                System.Buffer.MemoryCopy(srcPtr, (void*)ptr, (long)_size, (long)dataSize);
            }
        }
        finally
        {
            Unmap();
        }
    }

    public IntPtr Map()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        void* mappedPtr;
        var result = _context.Vk.MapMemory(_context.Device, _memory, 0, _size, 0, &mappedPtr);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Failed to map buffer memory: {result}");
        }

        return (IntPtr)mappedPtr;
    }

    public void Unmap()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _context.Vk.UnmapMemory(_context.Device, _memory);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var vk = _context.Vk;
        var device = _context.Device;

        if (_buffer.Handle != 0)
        {
            vk.DestroyBuffer(device, _buffer, null);
        }

        if (_memory.Handle != 0)
        {
            vk.FreeMemory(device, _memory, null);
        }
    }
}
