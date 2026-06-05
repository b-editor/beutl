# Dispatcher priority queue: `Channel.CreateUnboundedPrioritized` vs bucketed `Queue[]`

## Summary

On the `yuto-trd/optimize-dispatcher` branch we evaluated replacing the
`Beutl.Threading` dispatcher's hand-written priority queue and idle-wait machinery
with `System.Threading.Channels.Channel.CreateUnboundedPrioritized`. **The change was
reverted: it was slower, not faster.** The original implementation — three
`Queue<DispatcherOperation>` buckets, one per `DispatchPriority` — wins on throughput.

Keep this note so the same "use a prioritized Channel to optimize the dispatcher" idea
is not re-attempted without remembering the measured outcome.

## What was compared

The public `Dispatcher` API (`Spawn` / `Invoke` / `Dispatch` / `Schedule`) is identical
in both versions, so a single benchmark (`tests/Beutl.Benchmarks/DispatcherBenchmark.cs`)
runs against both. The "after" implementation was toggled off with
`git stash push -- src/Beutl.Threading` to measure "before", then restored.

- **before**: `OperationQueue` backed by `Queue<DispatcherOperation>[3]`, idle-wait via a
  per-cycle `CancellationTokenSource` + `WaitHandle.WaitOne()`, woken by `Post` under a lock.
- **after**: `OperationQueue` backed by `Channel.CreateUnboundedPrioritized` (a binary-heap
  `PriorityQueue` internally) with a sequence-number tiebreak for FIFO; idle-wait via
  `ChannelReader.WaitToReadAsync`.

## Results (Apple M3, .NET 10.0.7, `OperationCount = 1000`)

| Benchmark | before (`Queue[3]`) | after (`Channel`) | Delta |
|---|---:|---:|---|
| `DispatchThenDrain` (single-producer Post throughput) | 133 µs ± 9 | 301 µs ± 13 | **~2.3× slower** |
| `ConcurrentDispatch` (multi-producer Post throughput) | 468 µs ± 29 | 571 µs ± 115 | ~22% slower |
| `InvokeSequential` (wake-up latency) | 7,059 µs ± 590 | 6,148 µs ± 1,429 | within noise |

Allocations:

| Benchmark | before | after |
|---|---:|---:|
| `InvokeSequential` | 521 KB | 750 KB (+44%) |
| `DispatchThenDrain` | 165 KB | 173 KB |
| `ConcurrentDispatch` | 168 KB | 176 KB |

(`InvokeSequential` has a very wide confidence interval because every call is a
cross-thread round-trip; treat its latency as "no measurable win", not a regression.)

## Why the Channel is slower here

1. **Structural (dominant).** `CreateUnboundedPrioritized` keeps a binary-heap
   `PriorityQueue`, so enqueue/dequeue is `O(log n)` plus a comparer call. With only
   **three** fixed priority levels, the original bucket array is `O(1)` and simply faster.
   A prioritized Channel pays off when priorities are many/continuous, not for a 3-level enum.
2. **Allocation.** Blocking the dispatcher thread on `WaitToReadAsync().AsTask()` allocates
   a `Task` on every idle cycle, which shows up as the +44 % allocation on `InvokeSequential`.
3. **Wake-up latency** — the actual goal of moving to a Channel — showed no measurable
   improvement.

## Takeaway

The value of a prioritized Channel for this dispatcher would have been *simpler/more correct
wait logic* (dropping the manual `CancellationTokenSource` dance and the `Post` lock), **not**
speed. Since the branch goal is to optimize, the bucketed `Queue[]` implementation was kept.

## Reproducing

```bash
# after (current implementation)
dotnet run -c Release -f net10.0 --project tests/Beutl.Benchmarks -- \
  --filter '*DispatcherBenchmark*' --artifacts /tmp/dispatcher-bench/after

# before (toggle the implementation off, benchmark code is public-API only so it still builds)
git stash push -- src/Beutl.Threading
dotnet run -c Release -f net10.0 --project tests/Beutl.Benchmarks -- \
  --filter '*DispatcherBenchmark*' --artifacts /tmp/dispatcher-bench/before
git stash pop
```
