using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using BEditor.Compute.Memory;
using BEditor.Compute.OpenCL;
using BEditor.Compute.Runtime;

namespace BEditor.Compute.PlatformLayer
{
    public unsafe class Context : ComputeObject
    {
        public Context(params Device[] devices)
        {
            Devices = devices;

            var status = (int)CLStatusCode.CL_SUCCESS;
            var devicePointers = (void**)Marshal.AllocCoTaskMem(devices.Length * IntPtr.Size);
            for (var i = 0; i < devices.Length; i++)
            {
                devicePointers[i] = devices[i].Pointer;
            }

            Pointer = CL.CreateContext(null, (uint)devices.Length, devicePointers, null, null, &status);
            status.CheckError();
        }

        public Device[] Devices { get; }

        public void* Pointer { get; }

        public CommandQueue CreateCommandQueue(Device device)
        {
            return new CommandQueue(this, device);
        }

        public CLProgram CreateProgram(string source)
        {
            return new CLProgram(source, this);
        }

        public SimpleMemory CreateSimpleMemory(long size)
        {
            return new SimpleMemory(this, size);
        }

        public SimpleMemory CreateSimpleMemory<T>(Span<T> data, long size) where T : unmanaged
        {
            fixed (void* ptr = data)
            {
                return new SimpleMemory(this, ptr, size);
            }
        }

        public MappingMemory CreateMappingMemory(long size)
        {
            return new MappingMemory(this, size);
        }

        public MappingMemory CreateMappingMemory<T>(Span<T> data, long size) where T : unmanaged
        {
            fixed (void* ptr = data)
            {
                return new MappingMemory(this, ptr, size);
            }
        }

        public SVMBuffer CreateSVMBuffer(long size, uint alignment)
        {
            return new SVMBuffer(this, size, alignment);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            CL.ReleaseContext(Pointer).CheckError();
        }
    }
}