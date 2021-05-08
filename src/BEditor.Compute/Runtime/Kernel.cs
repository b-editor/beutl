using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using BEditor.Compute.Memory;
using BEditor.Compute.OpenCL;

namespace BEditor.Compute.Runtime
{
    public unsafe class Kernel : ComputeObject
    {
        private void*[] _args = new void*[0];
        private uint _dimention = 1;
        private readonly IntPtr* _workSizes = (IntPtr*)Marshal.AllocCoTaskMem(3 * IntPtr.Size);

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

        public string KernelName { get; }

        public CLProgram Program { get; }

        public void* Pointer { get; }

        public void SetArgs(params object[] args)
        {
            foreach (var arg in _args)
            {
                Marshal.FreeCoTaskMem(new IntPtr(arg));
            }

            _args = new void*[args.Length];

            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == null)
                {
                    throw new NullReferenceException();
                }
                else if (args[i] is AbstractMemory mem)
                {
                    var argPointer = (void*)Marshal.AllocCoTaskMem(IntPtr.Size);
                    Marshal.WriteIntPtr(new IntPtr(argPointer), new IntPtr(mem.Pointer));
                    CL.SetKernelArg(Pointer, i, IntPtr.Size, argPointer).CheckError();
                    _args[i] = argPointer;
                }
                else if (args[i] is SVMBuffer buf)
                {
                    CL.SetKernelArgSVMPointer(Pointer, i, buf.Pointer).CheckError();
                }
                else if (args[i] is byte barg)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(byte), &barg).CheckError();
                }
                else if (args[i] is char carg)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(char), &carg).CheckError();
                }
                else if (args[i] is short sarg)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(short), &sarg).CheckError();
                }
                else if (args[i] is int iarg)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(int), &iarg).CheckError();
                }
                else if (args[i] is long larg)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(long), &larg).CheckError();
                }
                else if (args[i] is float farg)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(float), &farg).CheckError();
                }
                else if (args[i] is double darg)
                {
                    CL.SetKernelArg(Pointer, i, sizeof(double), &darg).CheckError();
                }
                else
                {
                    throw new NotSupportedException("未対応の型です");
                }
            }
        }

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

        public Event NDRange(CommandQueue commandQueue, long[] workSizes)
        {
            SetWorkSize(workSizes);
            return NDRange(commandQueue);
        }

        public Event NDRange(CommandQueue commandQueue, long[] workSizes, params object[] args)
        {
            SetWorkSize(workSizes);
            SetArgs(args);
            return NDRange(commandQueue);
        }

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