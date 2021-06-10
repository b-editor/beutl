// Event.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Threading.Tasks;

using BEditor.Compute.OpenCL;

namespace BEditor.Compute.Runtime
{
    /// <summary>
    /// Represents the OpenCL event.
    /// </summary>
    public unsafe class Event
    {
        private long? _executionTime = null;

        internal Event(void* pointer)
        {
            Pointer = pointer;
        }

        /// <summary>
        /// Gets the execution time.
        /// </summary>
        public long ExecutionTime
        {
            get
            {
                if (_executionTime == null)
                {
                    Wait();

                    long start;
                    long end;
                    _ = CL.GetEventProfilingInfo(Pointer, CLProfilingInfo.CL_PROFILING_COMMAND_START, new IntPtr(sizeof(long)), &start, null);
                    _ = CL.GetEventProfilingInfo(Pointer, CLProfilingInfo.CL_PROFILING_COMMAND_END, new IntPtr(sizeof(long)), &end, null);
                    _executionTime = end - start;
                }

                return (long)_executionTime;
            }
        }

        /// <summary>
        /// Gets the pointer.
        /// </summary>
        public void* Pointer { get; }

        /// <summary>
        /// Waits on the host thread for commands identified by event objects to complete.
        /// </summary>
        public void Wait()
        {
            var event_ = Pointer;
            _ = CL.WaitForEvents(1, &event_);
        }
    }
}