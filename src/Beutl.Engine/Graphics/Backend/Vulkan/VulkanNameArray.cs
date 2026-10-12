using System.Runtime.InteropServices;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Null-terminated ANSI copies of the extension or layer names a Vulkan create info enables, freed on dispose.
/// </summary>
internal readonly unsafe struct VulkanNameArray : IDisposable
{
    public VulkanNameArray(IReadOnlyList<string> names)
    {
        Pointers = new byte*[names.Count];
        try
        {
            for (int i = 0; i < Pointers.Length; i++)
                Pointers[i] = (byte*)Marshal.StringToHGlobalAnsi(names[i]);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>The copies in order, to be pinned as a <c>ppEnabled*Names</c> array.</summary>
    public byte*[] Pointers { get; }

    public uint Count => (uint)Pointers.Length;

    public void Dispose()
    {
        // An entry not yet copied is null, which FreeHGlobal ignores.
        for (int i = 0; i < Pointers.Length; i++)
        {
            Marshal.FreeHGlobal((IntPtr)Pointers[i]);
            Pointers[i] = null;
        }
    }
}
