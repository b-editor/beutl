using BeUtl.Compute.OpenCL;

namespace BeUtl.Compute.Runtime;

public unsafe class Event
{
    private long? _executionTime = null;

    internal Event(void* pointer)
    {
        Pointer = pointer;
    }

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

    public void* Pointer { get; }

    public void Wait()
    {
        var event_ = Pointer;
        _ = CL.WaitForEvents(1, &event_);
    }
}
