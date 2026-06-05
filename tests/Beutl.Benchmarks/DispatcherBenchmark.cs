using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

using Beutl.Threading;

namespace Beutl.Benchmarks;

// Measures the dispatcher message loop across three axes: per-operation wake-up latency,
// single-producer Post throughput, and multi-producer (contended) Post throughput, plus
// allocations. Useful when evaluating changes to OperationQueue / the idle-wait machinery.
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

    // Cross-thread Invoke in a tight loop: each call leaves the dispatcher idle and must
    // wake it again, so this measures the per-operation wake-up latency.
    [Benchmark]
    public void InvokeSequential()
    {
        for (int i = 0; i < OperationCount; i++)
        {
            _dispatcher.Invoke(static () => { });
        }
    }

    // Fire-and-forget posts from a single producer, then a Low-priority barrier that only
    // runs once every Medium operation has drained. Measures Post throughput.
    [Benchmark]
    public void DispatchThenDrain()
    {
        for (int i = 0; i < OperationCount; i++)
        {
            _dispatcher.Dispatch(static () => { });
        }

        _dispatcher.Invoke(static () => { }, DispatchPriority.Low);
    }

    // Many producer threads posting concurrently. This is where removing the per-Post
    // lock + CancellationTokenSource churn should show up most.
    [Benchmark]
    public void ConcurrentDispatch()
    {
        Dispatcher dispatcher = _dispatcher;
        Parallel.For(0, OperationCount, _ => dispatcher.Dispatch(static () => { }));
        dispatcher.Invoke(static () => { }, DispatchPriority.Low);
    }
}
