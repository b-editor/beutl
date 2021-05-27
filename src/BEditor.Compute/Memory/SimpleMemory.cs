// SimpleMemory.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Compute.OpenCL;
using BEditor.Compute.PlatformLayer;

namespace BEditor.Compute.Memory
{
    /// <summary>
    /// Represents the OpenCL memory using CL_MEM.
    /// </summary>
    public unsafe class SimpleMemory : AbstractMemory
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleMemory"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="size">The number of bytes in memory.</param>
        public SimpleMemory(Context context, long size)
        {
            Size = size;
            Context = context;
            var status = (int)CLStatusCode.CL_SUCCESS;
            Pointer = CL.CreateBuffer(context.Pointer, CLMemoryFlags.CL_MEM_READ_WRITE, new IntPtr(size), null, &status);
            status.CheckError();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleMemory"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="data">A pointer to the buffer data that may already be allocated by the application.</param>
        /// <param name="size">The number of bytes in memory.</param>
        public SimpleMemory(Context context, IntPtr data, long size)
        {
            CreateSimpleMemory(context, (void*)data, size);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleMemory"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="data">A pointer to the buffer data that may already be allocated by the application.</param>
        /// <param name="size">The number of bytes in memory.</param>
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
}