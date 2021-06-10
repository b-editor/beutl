// SVMBuffer.cs
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
    /// Represents the OpenCL SVM buffer.
    /// </summary>
    public unsafe class SVMBuffer : AbstractBuffer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SVMBuffer"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="size">The number of bytes in buffer.</param>
        /// <param name="alignment">The minimum alignment in bytes that is required for the newly created buffer’s memory region.</param>
        public SVMBuffer(Context context, long size, uint alignment)
        {
            Size = size;
            Context = context;
            Pointer = CL.SVMAlloc(context.Pointer, CLMemoryFlags.CL_MEM_READ_WRITE, new IntPtr(size), alignment);
        }

        /// <summary>
        /// Gets the number of bytes in buffer.
        /// </summary>
        public long Size { get; }

        /// <summary>
        /// Gets the context.
        /// </summary>
        public Context Context { get; }

        /// <summary>
        /// Gets the SVM pointer.
        /// </summary>
        public void* Pointer { get; }

        /// <summary>
        /// Gets the <see cref="Span{T}"/> of the SVM pointer.
        /// </summary>
        /// <typeparam name="T">The type of the <see cref="Span{T}"/> element.</typeparam>
        /// <returns>The Span of the SVM pointer.</returns>
        public Span<T> GetSVMPointer<T>()
            where T : unmanaged
        {
            return new Span<T>(Pointer, (int)(Size / sizeof(T)));
        }

        /// <summary>
        /// Maps the region of the specified buffer object to the host's address space and returns a pointer to this mapped region.
        /// </summary>
        /// <param name="commandQueue">Must be a valid command-queue.</param>
        /// <param name="blocking">Indicates if the map operation is blocking or non-blocking.</param>
        /// <returns>Returns an event object that identifies this particular copy command and can be used toquery or queue a wait for this particular command to complete.</returns>
        public Event Mapping(CommandQueue commandQueue, bool blocking)
        {
            void* event_ = null;
            CL.EnqueueSVMMap(commandQueue.Pointer, blocking, CLMapFlags.CL_MAP_READ | CLMapFlags.CL_MAP_WRITE, Pointer, new IntPtr(Size), 0, null, &event_).CheckError();
            return new Event(event_);
        }

        /// <summary>
        /// Unmaps a previously mapped region of a memory object.
        /// </summary>
        /// <param name="commandQueue">Must be a valid command-queue.</param>
        /// <returns>Returns an event object that identifies this particular copy command and can be used to query or queue a wait for this particular command to complete.</returns>
        public Event UnMapping(CommandQueue commandQueue)
        {
            void* event_ = null;
            CL.EnqueueSVMUnmap(commandQueue.Pointer, Pointer, 0, null, &event_).CheckError();
            return new Event(event_);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            CL.SVMFree(Context.Pointer, Pointer);
        }
    }
}