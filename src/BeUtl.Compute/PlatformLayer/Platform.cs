using System.Runtime.InteropServices;
using BeUtl.Compute.OpenCL;

namespace BeUtl.Compute.PlatformLayer;

public unsafe class Platform
{
    static Platform()
    {
        // get platforms
        uint count = 0;
        CL.GetPlatformIDs(0, null, &count).CheckError();

        // create platform infos
        for (var i = 0; i < count; i++)
        {
            PlatformInfos.Add(new PlatformInfo(i));
        }
    }

    public Platform(int index)
    {
        Index = index;

        // get a platform
        uint count = 0;
        CL.GetPlatformIDs(0, null, &count).CheckError();
        var platforms = (void**)NativeMemory.Alloc((nuint)(count * IntPtr.Size));

        try
        {
            CL.GetPlatformIDs(count, platforms, &count).CheckError();
            Pointer = platforms[index];
        }
        finally
        {
            NativeMemory.Free(platforms);
        }

        Info = PlatformInfos[index];
    }

    public static List<PlatformInfo> PlatformInfos { get; } = new List<PlatformInfo>();

    public int Index { get; }

    public void* Pointer { get; }

    public PlatformInfo Info { get; }

    public Device[] CreateDevices(params int[] indices)
    {
        return indices
            .Select(i => new Device(this, i))
            .ToArray();
    }
}
