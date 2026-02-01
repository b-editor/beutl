using System;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Specifies memory properties for buffer allocation.
/// </summary>
[Flags]
public enum MemoryProperty
{
    /// <summary>
    /// No specific properties.
    /// </summary>
    None = 0,

    /// <summary>
    /// Memory is local to the GPU device (fastest for GPU access).
    /// </summary>
    DeviceLocal = 1 << 0,

    /// <summary>
    /// Memory is visible to the host CPU.
    /// </summary>
    HostVisible = 1 << 1,

    /// <summary>
    /// Memory is coherent between host and device (no explicit flush needed).
    /// </summary>
    HostCoherent = 1 << 2,

    /// <summary>
    /// Memory is cached on the host.
    /// </summary>
    HostCached = 1 << 3,

    /// <summary>
    /// Memory can be lazily allocated.
    /// </summary>
    LazilyAllocated = 1 << 4,
}
