using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using BEditor.Compute.OpenCL;
using BEditor.Compute.PlatformLayer;

namespace BEditor.Compute.Runtime
{
    public unsafe class CLProgram : ComputeObject
    {
        public CLProgram(string source, Context context)
        {
            Context = context;

            var status = (int)CLStatusCode.CL_SUCCESS;
            var sourceArray = Encoding.UTF8.GetBytes(source);
            var lengths = (void*)(new IntPtr(source.Length));

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

        public Context Context { get; }

        public void* Pointer { get; }

        public Kernel CreateKernel(string kernelName)
        {
            return new Kernel(this, kernelName);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            CL.ReleaseProgram(Pointer).CheckError();
        }
    }
}