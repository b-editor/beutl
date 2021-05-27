// CLProgram.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Runtime.InteropServices;
using System.Text;

using BEditor.Compute.OpenCL;
using BEditor.Compute.PlatformLayer;

namespace BEditor.Compute.Runtime
{
    /// <summary>
    /// Represents the OpenCL program.
    /// </summary>
    public unsafe class CLProgram : ComputeObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CLProgram"/> class.
        /// </summary>
        /// <param name="source">The OpenCL C source code.</param>
        /// <param name="context">The context.</param>
        public CLProgram(string source, Context context)
        {
            Context = context;

            var status = (int)CLStatusCode.CL_SUCCESS;
            var sourceArray = Encoding.UTF8.GetBytes(source);
            var lengths = (void*)new IntPtr(source.Length);

            fixed (byte* sourceArrayPointer = sourceArray)
            {
                var sourcesArray = new byte*[] { sourceArrayPointer };

                fixed (byte** sourcesArrayPointer = sourcesArray)
                {
                    Pointer = CL.CreateProgramWithSource(context.Pointer, 1, sourcesArrayPointer, &lengths, &status);
                    status.CheckError();
                }
            }

            try
            {
                var devices = context.Devices;
                var devicePointers = (void**)Marshal.AllocCoTaskMem(devices.Length * IntPtr.Size);

                for (var i = 0; i < devices.Length; i++)
                {
                    devicePointers[i] = devices[i].Pointer;
                }

                CL.BuildProgram(Pointer, (uint)devices.Length, devicePointers, null, null, null).CheckError();
            }
            catch (Exception e)
            {
                long logSize;
                CL.GetProgramBuildInfo(Pointer, context.Devices[0].Pointer, CLProgramBuildInfo.CL_PROGRAM_BUILD_LOG, IntPtr.Zero, null, &logSize).CheckError();
                var log = new byte[logSize + 1];

                fixed (byte* logPointer = log)
                {
                    CL.GetProgramBuildInfo(Pointer, context.Devices[0].Pointer, CLProgramBuildInfo.CL_PROGRAM_BUILD_LOG, new IntPtr(logSize), logPointer, null).CheckError();
                }

                _ = CL.ReleaseProgram(Pointer);

                throw new Exception(e.Message + Environment.NewLine + Encoding.UTF8.GetString(log, 0, (int)logSize));
            }
        }

        /// <summary>
        /// Gets the context.
        /// </summary>
        public Context Context { get; }

        /// <summary>
        /// Gets the pointer.
        /// </summary>
        public void* Pointer { get; }

        /// <summary>
        /// Creates the kernel.
        /// </summary>
        /// <param name="kernelName">The kernel name.</param>
        /// <returns>Returns the created <see cref="Kernel"/>.</returns>
        public Kernel CreateKernel(string kernelName)
        {
            return new Kernel(this, kernelName);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            CL.ReleaseProgram(Pointer).CheckError();
        }
    }
}