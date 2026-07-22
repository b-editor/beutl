```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Apple M3, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.301
  [Host]                      : .NET 10.0.9 (10.0.9, 10.0.926.27113), Arm64 RyuJIT armv8.0-a
  TargetBaselinePersistentGpu : .NET 10.0.9 (10.0.9, 10.0.926.27113), Arm64 RyuJIT armv8.0-a

Job=TargetBaselinePersistentGpu  InvocationCount=1  IterationCount=15  
LaunchCount=1  RunStrategy=Monitoring  UnrollFactor=1  
WarmupCount=3  

```
| Method              | CaseName             | Mean       | Error      | StdDev     | Median     | Allocated |
|-------------------- |--------------------- |-----------:|-----------:|-----------:|-----------:|----------:|
| **CompleteTargetFrame** | **NoEffectControl**      |   **924.9 μs** |   **604.9 μs** |   **565.8 μs** |   **726.2 μs** |   **2.72 KB** |
| **CompleteTargetFrame** | **ShaderOpacityShader**  | **3,072.4 μs** |   **741.9 μs** |   **694.0 μs** | **2,765.5 μs** |   **32.8 KB** |
| **CompleteTargetFrame** | **Shade(...)rrier [26]** | **4,016.7 μs** | **1,070.0 μs** | **1,000.8 μs** | **3,749.6 μs** |  **44.38 KB** |
