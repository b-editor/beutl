using BEditorNext.Compute.OpenCL;
using BEditorNext.Compute.PlatformLayer;
using BEditorNext.Compute.Runtime;

namespace BEditorNext.Compute.Memory;

public unsafe class MappingMemory : AbstractMemory
{
    public MappingMemory(Context context, long size)
    {
        CreateMappingMemory(context, size);
    }

    public MappingMemory(Context context, IntPtr data, long size)
    {
        CreateMappingMemory(context, (void*)data, size);
    }

    public MappingMemory(Context context, void* data, long size)
    {
        CreateMappingMemory(context, data, size);
    }

    public Event Mapping(CommandQueue commandQueue, bool blocking, long offset, long size, out void* pointer)
    {
        int status;
        void* event_ = null;
        pointer = CL.EnqueueMapBuffer(commandQueue.Pointer, Pointer, blocking, CLMapFlags.CL_MAP_READ | CLMapFlags.CL_MAP_WRITE, new IntPtr(offset), new IntPtr(size), 0, null, &event_, &status);
        status.CheckError();
        return new Event(event_);
    }

    public Event UnMapping(CommandQueue commandQueue, void* pointer)
    {
        void* event_ = null;
        CL.EnqueueUnmapMemObject(commandQueue.Pointer, Pointer, pointer, 0, null, &event_).CheckError();
        return new Event(event_);
    }

    private void CreateMappingMemory(Context context, long size)
    {
        int status;
        Pointer = CL.CreateBuffer(context.Pointer, CLMemoryFlags.CL_MEM_ALLOC_HOST_PTR | CLMemoryFlags.CL_MEM_READ_WRITE, new IntPtr(size), null, &status);
        Size = size;
        Context = context;
        status.CheckError();
    }

    private void CreateMappingMemory(Context context, void* dataPointer, long size)
    {
        int status;
        Pointer = CL.CreateBuffer(context.Pointer, CLMemoryFlags.CL_MEM_USE_HOST_PTR | CLMemoryFlags.CL_MEM_READ_WRITE, new IntPtr(size), dataPointer, &status);
        Size = size;
        Context = context;
        status.CheckError();
    }
}
