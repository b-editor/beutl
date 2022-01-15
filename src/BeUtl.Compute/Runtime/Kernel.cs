using System.Runtime.InteropServices;
using System.Text;

using BeUtl.Compute.Memory;
using BeUtl.Compute.OpenCL;

namespace BeUtl.Compute.Runtime;

public unsafe class Kernel : ComputeObject
{
    private readonly IntPtr* _workSizes = (IntPtr*)NativeMemory.Alloc((nuint)(3 * IntPtr.Size));
    private void*[] _args = new void*[0];
    private uint _dimention = 1;

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
            NativeMemory.Free(arg);
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
                var argPointer = NativeMemory.Alloc((nuint)IntPtr.Size);
                Marshal.WriteIntPtr(new IntPtr(argPointer), new IntPtr(mem.Pointer));
                CL.SetKernelArg(Pointer, i, IntPtr.Size, argPointer).CheckError();
                _args[i] = argPointer;
            }
            else if (arg is SVMBuffer buf)
            {
                CL.SetKernelArgSVMPointer(Pointer, i, buf.Pointer).CheckError();
            }
            else if (arg is ValueType)
            {
                var size = Marshal.SizeOf(arg);
                var ptr = NativeMemory.Alloc((nuint)size);
                Marshal.StructureToPtr(arg, (IntPtr)ptr, false);
                CL.SetKernelArg(Pointer, i, size, ptr).CheckError();
                _args[i] = ptr;
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
            NativeMemory.Free(arg);
        }

        CL.ReleaseKernel(Pointer).CheckError();
    }
}
