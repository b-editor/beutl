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
| Method              | CaseName             | Mean      | Error      | StdDev     | Median    | Allocated |
|-------------------- |--------------------- |----------:|-----------:|-----------:|----------:|----------:|
| **CompleteTargetFrame** | **NoEffectControl**      |  **2.059 ms** |  **0.9755 ms** |  **0.9125 ms** |  **1.734 ms** |   **2.76 KB** |
| **CompleteTargetFrame** | **ShaderOpacityShader**  |  **5.860 ms** |  **2.6229 ms** |  **2.4535 ms** |  **5.098 ms** |     **34 KB** |
| **CompleteTargetFrame** | **Shade(...)rrier [26]** | **17.014 ms** | **17.3024 ms** | **16.1847 ms** | **11.677 ms** |  **46.15 KB** |
