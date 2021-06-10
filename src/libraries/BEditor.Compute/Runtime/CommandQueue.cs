// CommandQueue.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Compute.OpenCL;
using BEditor.Compute.PlatformLayer;

namespace BEditor.Compute.Runtime
{
    /// <summary>
    /// Represents the OpenCL command queue.
    /// </summary>
    public unsafe class CommandQueue : ComputeObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CommandQueue"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="device">The device.</param>
        public CommandQueue(Context context, Device device)
        {
            var status = (int)CLStatusCode.CL_SUCCESS;
            Context = context;
            Device = device;
            Pointer = CL.CreateCommandQueue(context.Pointer, device.Pointer, CLCommandQueueProperties.CL_QUEUE_PROFILING_ENABLE, &status);
            status.CheckError();
        }

        /// <summary>
        /// Gets the context.
        /// </summary>
        public Context Context { get; }

        /// <summary>
        /// Gets the device.
        /// </summary>
        public Device Device { get; }

        /// <summary>
        /// Gets the pointer.
        /// </summary>
        public void* Pointer { get; }

        /// <summary>
        /// Execute a kernel on a device.
        /// </summary>
        /// <param name="kernel">The kernel object.</param>
        /// <param name="eventWaitList">Specify events that need to complete before this particular command can be executed.</param>
        /// <returns>Returns an event object that identifies this particular kernel execution instance.</returns>
        public Event NDRangeKernel(Kernel kernel, params Event[] eventWaitList)
        {
            return kernel.NDRange(this, eventWaitList);
        }

        /// <summary>
        /// Blocks until all previously queued OpenCL commands in a command-queue are issued to the associated device and have completed.
        /// </summary>
        public void WaitFinish()
        {
            CL.Finish(Pointer).CheckError();
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            CL.ReleaseCommandQueue(Pointer).CheckError();
        }
    }
}