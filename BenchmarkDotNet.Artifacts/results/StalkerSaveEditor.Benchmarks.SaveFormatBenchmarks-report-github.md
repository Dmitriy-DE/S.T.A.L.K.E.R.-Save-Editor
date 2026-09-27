```

BenchmarkDotNet v0.15.8, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
AMD Ryzen 5 4500U with Radeon Graphics 1.40GHz, 1 CPU, 6 logical and 6 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | Mean     | Error     | StdDev    | Gen0   | Allocated |
|--------- |---------:|----------:|----------:|-------:|----------:|
| ParseSoc | 1.443 μs | 0.6784 μs | 0.0372 μs | 1.7128 |    3.5 KB |
