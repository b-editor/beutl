using System;
using System.Linq;

using BEditor.Compute.OpenCL;
using BEditor.Compute.PlatformLayer;
using BEditor.Compute.Runtime;

namespace BEditor.Compute.Memory
{
    public abstract unsafe class AbstractMemory : AbstractBuffer
    {
        public long Size { get; protected set; }

        public Context? Context { get; protected set; }

        public void* Pointer { get; protected set; }

        public Event Read<T>(CommandQueue commandQueue, bool blocking, Span<T> data, long offset, long size, params Event[] eventWaitList) where T : unmanaged
        {
            fixed (void* dataPointer = data)
            {
                return Read(commandQueue, blocking, dataPointer, offset, size, eventWaitList);
            }
        }

        public Event Read(CommandQueue commandQueue, bool blocking, IntPtr data, long offset, long size, params Event[] eventWaitList)
        {
            return Read(commandQueue, blocking, (void*)data, offset, size, eventWaitList);
        }

        public Event Read(CommandQueue commandQueue, bool blocking, void* data, long offset, long size, params Event[] eventWaitList)
        {
            ThrowIfDisposed();

            void* event_ = null;

            var num = (uint)eventWaitList.Length;
            var list = eventWaitList.Select(e => new IntPtr(e.Pointer)).ToArray();
            fixed (void* listPointer = list)
            {
                CL.EnqueueReadBuffer(commandQueue.Pointer, Pointer, blocking, new IntPtr(offset), new IntPtr(size), data, num, listPointer, &event_).CheckError();
            }

            return new Event(event_);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            CL.ReleaseMemObject(Pointer).CheckError();
        }
    }
}