using BeUtl.Compute.OpenCL;
using BeUtl.Compute.PlatformLayer;

namespace BeUtl.Compute.Memory;

public unsafe class SimpleMemory : AbstractMemory
{
    public SimpleMemory(Context context, long size)
    {
        Size = size;
        Context = context;
        var status = (int)CLStatusCode.CL_SUCCESS;
        Pointer = CL.CreateBuffer(context.Pointer, CLMemoryFlags.CL_MEM_READ_WRITE, new IntPtr(size), null, &status);
        status.CheckError();
    }

    public SimpleMemory(Context context, IntPtr data, long size)
    {
        CreateSimpleMemory(context, (void*)data, size);
    }

    public SimpleMemory(Context context, void* data, long size)
    {
        CreateSimpleMemory(context, data, size);
    }

    private void CreateSimpleMemory(Context context, void* dataPointer, long size)
    {
        Size = size;
        Context = context;

        var status = (int)CLStatusCode.CL_SUCCESS;
        Pointer = CL.CreateBuffer(context.Pointer, CLMemoryFlags.CL_MEM_COPY_HOST_PTR | CLMemoryFlags.CL_MEM_READ_WRITE, new IntPtr(size), dataPointer, &status);
        status.CheckError();
    }
}
