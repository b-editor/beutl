using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering.Requests;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Shaders;

/// <summary>
/// Keeps the images shader graph executions finish with, so a later execution on the same context takes one
/// instead of allocating it again.
/// </summary>
/// <remarks>
/// The least recently returned images are disposed first once the retained bytes pass the budget, and an image no
/// execution took for <c>maximumIdleRequests</c> requests is disposed, as <see cref="RenderTargetPool"/> does. An image
/// keeps the layouts it recorded; a taking execution synchronizes with the work that last used it before its first command.
/// </remarks>
internal sealed class ShaderGraphImagePool(
    // The render target pool's defaults, so both pools let go of an effect's memory at the same time.
    long maximumRetainedBytes = RenderTargetPoolOptions.DefaultMaximumRetainedBytes,
    int maximumIdleRequests = RenderTargetPoolOptions.DefaultMaximumIdleRequests) : IDisposable
{
    private readonly Lock _gate = new();
    // Oldest first, so eviction takes from the front and a lookup prefers the most recent match.
    private readonly LinkedList<(ShaderGraphImage Image, long ReturnedRequest)> _retained = [];
    private long _retainedBytes;
    private long _request;
    private bool _disposed;

    internal long RetainedBytes
    {
        get
        {
            lock (_gate)
                return _retainedBytes;
        }
    }

    /// <summary>Takes an image of <paramref name="description"/> on <paramref name="context"/> that supports
    /// every <paramref name="usage"/>, or returns <see langword="null"/>.</summary>
    public ShaderGraphImage? Take(VulkanContext context, ImageDescription description, ImageUsageFlags usage)
    {
        lock (_gate)
        {
            for (LinkedListNode<(ShaderGraphImage Image, long ReturnedRequest)>? node = _retained.Last;
                 node is not null; node = node.Previous)
            {
                ShaderGraphImage image = node.Value.Image;
                if (image.Context != context || !image.Supports(description, usage))
                    continue;

                _retained.Remove(node);
                _retainedBytes -= (long)image.AllocationSize;
                return image;
            }
        }

        return null;
    }

    /// <summary>Starts a render request, and disposes the images no execution took for too many requests.</summary>
    public void BeginRequest()
    {
        List<ShaderGraphImage> idle = [];
        lock (_gate)
        {
            _request++;
            while (_retained.First is { } node && _request - node.Value.ReturnedRequest > maximumIdleRequests)
            {
                _retained.RemoveFirst();
                _retainedBytes -= (long)node.Value.Image.AllocationSize;
                idle.Add(node.Value.Image);
            }
        }

        foreach (ShaderGraphImage image in idle)
            image.Dispose();
    }

    /// <summary>Keeps <paramref name="image"/> for a later execution, or disposes it when it does not fit.</summary>
    public void Return(ShaderGraphImage image)
    {
        List<ShaderGraphImage> evicted = [];
        lock (_gate)
        {
            if (_disposed || (long)image.AllocationSize > maximumRetainedBytes)
            {
                evicted.Add(image);
            }
            else
            {
                _retained.AddLast((image, _request));
                _retainedBytes += (long)image.AllocationSize;
                while (_retainedBytes > maximumRetainedBytes)
                {
                    ShaderGraphImage oldest = _retained.First!.Value.Image;
                    _retained.RemoveFirst();
                    _retainedBytes -= (long)oldest.AllocationSize;
                    evicted.Add(oldest);
                }
            }
        }

        foreach (ShaderGraphImage stale in evicted)
            stale.Dispose();
    }

    /// <summary>Disposes every retained image, as when the device context they were allocated on goes away.</summary>
    public void Clear()
    {
        ShaderGraphImage[] retained;
        lock (_gate)
        {
            retained = [.. _retained.Select(static entry => entry.Image)];
            _retained.Clear();
            _retainedBytes = 0;
        }

        foreach (ShaderGraphImage image in retained)
            image.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
            _disposed = true;
        Clear();
    }
}
