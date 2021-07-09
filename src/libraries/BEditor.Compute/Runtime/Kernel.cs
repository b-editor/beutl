// Kernel.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using BEditor.Compute.Memory;
using BEditor.Compute.OpenCL;

namespace BEditor.Compute.Runtime
{
    /// <summary>
    /// Represents the OpenCL kernel.
    /// </summary>
    public unsafe class Kernel : ComputeObject
    {
        private readonly IntPtr* _workSizes = (IntPtr*)Marshal.AllocCoTaskMem(3 * IntPtr.Size);
        private void*[] _args = new void*[0];
        private uint _dimention = 1;

        /// <summary>
        /// Initializes a new instance of the <see cref="Kernel"/> class.
        /// </summary>
        /// <param name="program">The program.</param>
        /// <param name="kernelName">The kernel name.</param>
        public Kernel(CLProgram program, string kernelName)
        {
            KernelName = kernelName;
            Program = program;

            var status = (int)CLStatusCode.CL_SUCCESS;
            var kernelNameArray = Encoding.UTF8.GetBytes(kernelName);

            fixed (byte* kernelNameArrayPointer = kernelNameArray)
            {
                Pointer = CL.CreateKernel(program.Pointer, kernelNameArrayPointer, &status);
                status.CheckError();
            }
        }

        /// <summary>
        /// Gets the kernel name.
        /// </summary>
        public string KernelName { get; }

        /// <summary>
        /// Gets the program.
        /// </summary>
        public CLProgram Program { get; }

        /// <summary>
        /// Gets the pointer.
        /// </summary>
        public void* Pointer { get; }

        /// <summary>
        /// Sets the arguments.
        /// </summary>
        /// <param name="args">The arguments.</param>
        public void SetArgs(params object[] args)
        {
            foreach (var arg in _args)
            {
                Marshal.FreeCoTaskMem(new IntPtr(arg));
            }

            _args = new void*[args.Length];

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == null)
                {
                    throw new NullReferenceException();
                }
                else if (arg is AbstractMemory mem)
                {
                    var argPointer = (void*)Marshal.AllocCoTaskMem(IntPtr.Size);
                    Marshal.WriteIntPtr(new IntPtr(argPointer), new IntPtr(mem.Pointer));
                    CL.SetKernelArg(Pointer, i, IntPtr.Size, argPointer).CheckError();
                    _args[i] = argPointer;
                }
                else if (arg is SVMBuffer buf)
                {
                    CL.SetKernelArgSVMPointer(Pointer, i, buf.Pointer).CheckError();
                }
                else if (arg is byte barg)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(byte), &barg).CheckError();
                }
                else if (arg is char carg)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(char), &carg).CheckError();
                }
                else if (arg is short sarg)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(short), &sarg).CheckError();
                }
                else if (arg is int iarg)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(int), &iarg).CheckError();
                }
                else if (arg is long larg)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(long), &larg).CheckError();
                }
                else if (arg is float farg)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(float), &farg).CheckError();
                }
                else if (arg is double darg)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(double), &darg).CheckError();
                }
                else if (arg is Double2 d2)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(Double2), &d2).CheckError();
                }
                else if (arg is Double3 d3)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(Double3), &d3).CheckError();
                }
                else if (arg is Double4 d4)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(Double4), &d4).CheckError();
                }
                else if (arg is Float2 f2)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(Float2), &f2).CheckError();
                }
                else if (arg is Float3 f3)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(Float3), &f3).CheckError();
                }
                else if (arg is Float4 f4)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(Float4), &f4).CheckError();
                }
                else if (arg is Float5x5 f5x5)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(Float5x5), &f5x5).CheckError();
                }
            }
        }

        /// <summary>
        /// Sets the work sizes.
        /// </summary>
        /// <param name="workSizes">The local work sizes.</param>
        public void SetWorkSize(params long[] workSizes)
        {
            if (workSizes.Length is <= 0 or >= 4)
            {
                throw new ArgumentException("workSizes length is invalid.");
            }

            _dimention = (uint)workSizes.Length;

            for (var i = 0; i < _dimention; i++)
            {
                _workSizes[i] = new IntPtr(workSizes[i]);
            }
        }

        /// <summary>
        /// Execute a kernel on a device.
        /// </summary>
        /// <param name="commandQueue">The command queue.</param>
        /// <param name="eventWaitList">Specify events that need to complete before this particular command can be executed.</param>
        /// <returns>Returns an event object that identifies this particular kernel execution instance.</returns>
        public Event NDRange(CommandQueue commandQueue, params Event[] eventWaitList)
        {
            void* event_ = null;
            var num = (uint)eventWaitList.Length;
            var list = eventWaitList.Select(e => new IntPtr(e.Pointer)).ToArray();

            fixed (void* listPointer = list)
            {
                CL.EnqueueNDRangeKernel(commandQueue.Pointer, Pointer, _dimention, null, _workSizes, null, num, listPointer, &event_).CheckError();
            }

            return new Event(event_);
        }

        /// <summary>
        /// Execute a kernel on a device.
        /// </summary>
        /// <param name="commandQueue">The command queue.</param>
        /// <param name="workSizes">The local work sizes.</param>
        /// <returns>Returns an event object that identifies this particular kernel execution instance.</returns>
        public Event NDRange(CommandQueue commandQueue, long[] workSizes)
        {
            SetWorkSize(workSizes);
            return NDRange(commandQueue);
        }

        /// <summary>
        /// Execute a kernel on a device.
        /// </summary>
        /// <param name="commandQueue">The command queue.</param>
        /// <param name="workSizes">The local work sizes.</param>
        /// <param name="args">The arguments.</param>
        /// <returns>Returns an event object that identifies this particular kernel execution instance.</returns>
        public Event NDRange(CommandQueue commandQueue, long[] workSizes, params object[] args)
        {
            SetWorkSize(workSizes);
            SetArgs(args);
            return NDRange(commandQueue);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            foreach (var arg in _args)
            {
                Marshal.FreeCoTaskMem(new IntPtr(arg));
            }

            CL.ReleaseKernel(Pointer).CheckError();
        }
    }
}