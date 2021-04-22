using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Compute.OpenCL;
using BEditor.Compute.PlatformLayer;
using BEditor.Compute.Runtime;

namespace BEditor.Compute.Memory
{
    public unsafe class SVMBuffer : AbstractBuffer
    {
        public SVMBuffer(Context context, long size, uint alignment)
        {
            Size = size;
            Context = context;
            Pointer = CL.SVMAlloc(context.Pointer, CLMemoryFlags.CL_MEM_READ_WRITE, new IntPtr(size), alignment);
        }

        public long Size { get; }

        public Context Context { get; }

        public void* Pointer { get; }

        public Span<T> GetSVMPointer<T>() where T : unmanaged
        {
            return new Span<T>(Pointer, (int)(Size / sizeof(T)));
        }

        public Event Mapping(CommandQueue commandQueue, bool blocking)
        {
            void* event_ = null;
            CL.EnqueueSVMMap(commandQueue.Pointer, blocking, (CLMapFlags.CL_MAP_READ | CLMapFlags.CL_MAP_WRITE), Pointer, new IntPtr(Size), 0, null, &event_).CheckError();
            return new Event(event_);
        }

        public Event UnMapping(CommandQueue commandQueue)
        {
            void* event_ = null;
            CL.EnqueueSVMUnmap(commandQueue.Pointer, Pointer, 0, null, &event_).CheckError();
            return new Event(event_);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            CL.SVMFree(Context.Pointer, Pointer);
        }
    }
}