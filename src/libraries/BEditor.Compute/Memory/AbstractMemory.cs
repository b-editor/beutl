// AbstractMemory.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Linq;

using BEditor.Compute.OpenCL;
using BEditor.Compute.PlatformLayer;
using BEditor.Compute.Runtime;

namespace BEditor.Compute.Memory
{
    /// <summary>
    /// Represents the memory that can be used with OpenCL.
    /// </summary>
    public abstract unsafe class AbstractMemory : AbstractBuffer
    {
        /// <summary>
        /// Gets or sets the number of bytes in memory.
        /// </summary>
        public long Size { get; protected set; }

        /// <summary>
        /// Gets or sets the context.
        /// </summary>
        public Context? Context { get; protected set; }

        /// <summary>
        /// Gets or sets the pointer.
        /// </summary>
        public void* Pointer { get; protected set; }

        /// <summary>
        /// Reads from a buffer object to host memory.
        /// </summary>
        /// <typeparam name="T">The type of the <see cref="Span{T}"/> element.</typeparam>
        /// <param name="commandQueue">Refers to the command-queue in which the read command will be queued. <paramref name="commandQueue"/> and buffer must be created with the same OpenCL context.</param>
        /// <param name="blocking">Indicates if the read operations are or non-blocking.</param>
        /// <param name="data">The pointer to buffer in host memory where data is to be read into.</param>
        /// <param name="offset">The offset in bytes in the buffer object to read from.</param>
        /// <param name="size">The size in bytes of data being read.</param>
        /// <param name="eventWaitList">specify events that need to complete before this particular command can be executed.</param>
        /// <returns>Returns an event object that identifies this particular read command and can be used to query or queue a wait for this particular command to complete.</returns>
        public Event Read<T>(CommandQueue commandQueue, bool blocking, Span<T> data, long offset, long size, params Event[] eventWaitList)
            where T : unmanaged
        {
            fixed (void* dataPointer = data)
            {
                return Read(commandQueue, blocking, dataPointer, offset, size, eventWaitList);
            }
        }

        /// <summary>
        /// Reads from a buffer object to host memory.
        /// </summary>
        /// <param name="commandQueue">Refers to the command-queue in which the read command will be queued. <paramref name="commandQueue"/> and buffer must be created with the same OpenCL context.</param>
        /// <param name="blocking">Indicates if the read operations are or non-blocking.</param>
        /// <param name="data">The pointer to buffer in host memory where data is to be read into.</param>
        /// <param name="offset">The offset in bytes in the buffer object to read from.</param>
        /// <param name="size">The size in bytes of data being read.</param>
        /// <param name="eventWaitList">specify events that need to complete before this particular command can be executed.</param>
        /// <returns>Returns an event object that identifies this particular read command and can be used to query or queue a wait for this particular command to complete.</returns>
        public Event Read(CommandQueue commandQueue, bool blocking, IntPtr data, long offset, long size, params Event[] eventWaitList)
        {
            return Read(commandQueue, blocking, (void*)data, offset, size, eventWaitList);
        }

        /// <summary>
        /// Reads from a buffer object to host memory.
        /// </summary>
        /// <param name="commandQueue">Refers to the command-queue in which the read command will be queued. <paramref name="commandQueue"/> and buffer must be created with the same OpenCL context.</param>
        /// <param name="blocking">Indicates if the read operations are or non-blocking.</param>
        /// <param name="data">The pointer to buffer in host memory where data is to be read into.</param>
        /// <param name="offset">The offset in bytes in the buffer object to read from.</param>
        /// <param name="size">The size in bytes of data being read.</param>
        /// <param name="eventWaitList">specify events that need to complete before this particular command can be executed.</param>
        /// <returns>Returns an event object that identifies this particular read command and can be used to query or queue a wait for this particular command to complete.</returns>
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

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            CL.ReleaseMemObject(Pointer).CheckError();
        }
    }
}