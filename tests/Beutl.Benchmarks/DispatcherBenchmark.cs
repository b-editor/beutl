using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

using Beutl.Threading;

namespace Beutl.Benchmarks;

// Dispatcher message-loop benchmarks: wake-up latency, single- and multi-producer Post
// throughput, plus allocations. Use when changing OperationQueue / the idle-wait machinery.
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, warmupCount: 3, iterationCount: 5)]
public class DispatcherBenchmark
{
    private Dispatcher _dispatcher = null!;

    [Params(1000)]
    public int OperationCount { get; set; }

    [GlobalSetup]
    public void Setup() => _dispatcher = Dispatcher.Spawn();

    [GlobalCleanup]
    public void Cleanup() => _dispatcher.Shutdown();

    // Each Invoke leaves the dispatcher idle and re-wakes it: measures per-operation wake-up latency.
    [Benchmark]
    public void InvokeSequential()
    {
        for (int i = 0; i < OperationCount; i++)
        {
            _dispatcher.Invoke(static () => { });
        }
    }

    // Single-producer posts, then a Low-priority barrier that drains them. Measures Post throughput.
    [Benchmark]
    public void DispatchThenDrain()
    {
        for (int i = 0; i < OperationCount; i++)
        {
            _dispatcher.Dispatch(static () => { });
        }

        _dispatcher.Invoke(static () => { }, DispatchPriority.Low);
    }

    // Many producers posting concurrently: where removing per-Post lock + CTS churn shows up most.
    [Benchmark]
    public void ConcurrentDispatch()
    {
        Dispatcher dispatcher = _dispatcher;
        Parallel.For(0, OperationCount, _ => dispatcher.Dispatch(static () => { }));
        dispatcher.Invoke(static () => { }, DispatchPriority.Low);
    }
}
