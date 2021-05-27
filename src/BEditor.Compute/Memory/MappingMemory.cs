// MappingMemory.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Compute.OpenCL;
using BEditor.Compute.PlatformLayer;
using BEditor.Compute.Runtime;

namespace BEditor.Compute.Memory
{
    /// <summary>
    /// Represents the OpenCL memory using CL_MEM_ALLOC_HOST_PTR.
    /// </summary>
    public unsafe class MappingMemory : AbstractMemory
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MappingMemory"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="size">The number of bytes in memory.</param>
        public MappingMemory(Context context, long size)
        {
            CreateMappingMemory(context, size);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MappingMemory"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="data">A pointer to the buffer data that may already be allocated by the application.</param>
        /// <param name="size">The number of bytes in memory.</param>
        public MappingMemory(Context context, IntPtr data, long size)
        {
            CreateMappingMemory(context, (void*)data, size);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MappingMemory"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="data">A pointer to the buffer data that may already be allocated by the application.</param>
        /// <param name="size">The number of bytes in memory.</param>
        public MappingMemory(Context context, void* data, long size)
        {
            CreateMappingMemory(context, data, size);
        }

        /// <summary>
        /// Maps the region of the specified buffer object to the host's address space and returns a pointer to this mapped region.
        /// </summary>
        /// <param name="commandQueue">Must be a valid command-queue.</param>
        /// <param name="blocking">Indicates if the map operation is blocking or non-blocking.</param>
        /// <param name="offset">The offset (in bytes) in the buffer object to be mapped.</param>
        /// <param name="size">The offset in bytes and the size of the region in the buffer object that is being mapped.</param>
        /// <param name="pointer">The size (in bytes) of the area in the buffer object to be mapped.</param>
        /// <returns>Returns an event object that identifies this particular copy command and can be used toquery or queue a wait for this particular command to complete.</returns>
        public Event Mapping(CommandQueue commandQueue, bool blocking, long offset, long size, out void* pointer)
        {
            int status;
            void* event_ = null;
            pointer = CL.EnqueueMapBuffer(commandQueue.Pointer, Pointer, blocking, CLMapFlags.CL_MAP_READ | CLMapFlags.CL_MAP_WRITE, new IntPtr(offset), new IntPtr(size), 0, null, &event_, &status);
            status.CheckError();
            return new Event(event_);
        }

        /// <summary>
        /// Unmaps a previously mapped region of a memory object.
        /// </summary>
        /// <param name="commandQueue">Must be a valid command-queue.</param>
        /// <param name="pointer">The host address returned by the previous <see cref="Mapping(CommandQueue, bool, long, long, out void*)"/> call.</param>
        /// <returns>Returns an event object that identifies this particular copy command and can be used to query or queue a wait for this particular command to complete.</returns>
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
}