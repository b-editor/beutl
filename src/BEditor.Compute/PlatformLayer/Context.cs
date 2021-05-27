// Context.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Runtime.InteropServices;

using BEditor.Compute.Memory;
using BEditor.Compute.OpenCL;
using BEditor.Compute.Runtime;

namespace BEditor.Compute.PlatformLayer
{
    /// <summary>
    /// Represents the OpenCL context.
    /// </summary>
    public unsafe class Context : ComputeObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Context"/> class.
        /// </summary>
        /// <param name="devices">The devices.</param>
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

        /// <summary>
        /// Gets the devices.
        /// </summary>
        public Device[] Devices { get; }

        /// <summary>
        /// Gets the pointer.
        /// </summary>
        public void* Pointer { get; }

        /// <summary>
        /// Creates the Command queue.
        /// </summary>
        /// <param name="device">The device.</param>
        /// <returns>Returns the created <see cref="CommandQueue"/>.</returns>
        public CommandQueue CreateCommandQueue(Device device)
        {
            return new CommandQueue(this, device);
        }

        /// <summary>
        /// Creates the Program.
        /// </summary>
        /// <param name="source">The OpenCL C source code.</param>
        /// <returns>Returns the created <see cref="CLProgram"/>.</returns>
        public CLProgram CreateProgram(string source)
        {
            return new CLProgram(source, this);
        }

        /// <summary>
        /// Creates the Simple memory.
        /// </summary>
        /// <param name="size">The number of bytes in memory.</param>
        /// <returns>Returns the created <see cref="SimpleMemory"/>.</returns>
        public SimpleMemory CreateSimpleMemory(long size)
        {
            return new SimpleMemory(this, size);
        }

        /// <summary>
        /// Creates the Simple memory.
        /// </summary>
        /// <typeparam name="T">The type of the <see cref="Span{T}"/> element.</typeparam>
        /// <param name="data">A pointer to the buffer data that may already be allocated by the application.</param>
        /// <param name="size">The number of bytes in memory.</param>
        /// <returns>Returns the created <see cref="SimpleMemory"/>.</returns>
        public SimpleMemory CreateSimpleMemory<T>(Span<T> data, long size)
            where T : unmanaged
        {
            fixed (void* ptr = data)
            {
                return new SimpleMemory(this, ptr, size);
            }
        }

        /// <summary>
        /// Creates the Mapping memory.
        /// </summary>
        /// <param name="size">The number of bytes in memory.</param>
        /// <returns>Returns the created <see cref="MappingMemory"/>.</returns>
        public MappingMemory CreateMappingMemory(long size)
        {
            return new MappingMemory(this, size);
        }

        /// <summary>
        /// Creates the Mapping memory.
        /// </summary>
        /// <typeparam name="T">The type of the <see cref="Span{T}"/> element.</typeparam>
        /// <param name="data">A pointer to the buffer data that may already be allocated by the application.</param>
        /// <param name="size">The number of bytes in memory.</param>
        /// <returns>Returns the created <see cref="MappingMemory"/>.</returns>
        public MappingMemory CreateMappingMemory<T>(Span<T> data, long size)
            where T : unmanaged
        {
            fixed (void* ptr = data)
            {
                return new MappingMemory(this, ptr, size);
            }
        }

        /// <summary>
        /// Creates the SVM buffer.
        /// </summary>
        /// <param name="size">The number of bytes in buffer.</param>
        /// <param name="alignment">The minimum alignment in bytes that is required for the newly created buffer’s memory region.</param>
        /// <returns>Returns the created <see cref="SVMBuffer"/>.</returns>
        public SVMBuffer CreateSVMBuffer(long size, uint alignment)
        {
            return new SVMBuffer(this, size, alignment);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            CL.ReleaseContext(Pointer).CheckError();
        }
    }
}