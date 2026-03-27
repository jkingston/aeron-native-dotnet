# Aeron P/Invoke Wrapper & Benchmark Suite

A high-performance .NET P/Invoke wrapper for the [Aeron](https://github.com/aeron-io/aeron) C client, with a benchmark suite comparing it against [Aeron.NET](https://github.com/AdaptiveConsulting/Aeron.NET) for HFT/low-latency use cases.

## Architecture

```
src/
  Aeron.Native/            .NET 8 P/Invoke wrapper for libaeron
    Interop/
      AeronNative.cs        Raw LibraryImport bindings (SuppressGCTransition on hot paths)
      AeronStructs.cs       Blittable struct definitions (zero-copy marshalling)
    AeronClient.cs          High-level client (connect, add pub/sub)
    AeronContext.cs          Configuration context (fluent API)
    Publication.cs           Offer/TryClaim with SuppressGCTransition
    Subscription.cs          Poll with UnmanagedCallersOnly function pointers
    Image.cs                 Per-publisher image access
    FragmentAssembler.cs     Fragment reassembly (wraps C assembler)
    HeaderReader.cs          Header value extraction

  Aeron.Benchmarks/         Benchmark harness
    Config/
      BenchmarkConfig.cs     Test parameters (message rate, size, profiles)
    Harness/
      LatencyBenchmark.cs         P/Invoke ping/pong RTT with HdrHistogram
      ThroughputBenchmark.cs      P/Invoke max message rate
      AeronNetLatencyBenchmark.cs Aeron.NET ping/pong RTT (comparison)
      AeronNetThroughputBenchmark.cs Aeron.NET max message rate (comparison)
    Micro/
      PInvokeOverheadBenchmarks.cs BenchmarkDotNet microbenchmarks
    Program.cs               CLI runner
```

## P/Invoke Design for Low Latency

### SuppressGCTransition
Hot-path functions (`Offer`, `TryClaim`, `BufferClaimCommit`, position reads) use `[SuppressGCTransition]` to skip the cooperative-to-preemptive GC mode switch. Only applied to functions that complete in <1us with no callbacks.

### UnmanagedCallersOnly Function Pointers
Fragment callbacks use `[UnmanagedCallersOnly]` with `delegate* unmanaged[Cdecl]` function pointers instead of managed delegates.

### LibraryImport Source Generator
All bindings use .NET 7+ `LibraryImport` (not `DllImport`). The source generator produces marshalling code at compile time.

### Zero-Copy Path
`TryClaim` + `BufferClaimCommit` writes directly into the publication's memory-mapped term buffer.

## Benchmark Methodology

Modeled on the [official Aeron benchmarks](https://github.com/aeron-io/benchmarks):

### Latency (Ping/Pong RTT)
- Publisher sends timestamped messages to an echo thread
- Echo thread reflects messages back unchanged
- Publisher records `(receiveTime - sendTimestamp)` into an HdrHistogram
- Reports p50, p90, p99, p99.9, p99.99, max, mean, stddev
- Controlled send rate (not flood)

### Throughput
- Publisher sends as fast as possible for configurable duration
- Subscriber counts fragments in a separate thread
- Reports per-second throughput and back-pressure events
- Warmup iterations discarded

### Microbenchmarks (BenchmarkDotNet)
- Isolated P/Invoke call overhead (NanoClock, EpochClock, ErrCode)
- Native vs managed Offer/TryClaim/Poll cost per call
- Memory allocation tracking via `[MemoryDiagnoser]`

### GC Considerations
- `SustainedLowLatency` GC mode suppresses Gen2 collections during measurement
- Optional `NoGCRegion` for ultra-critical paths
- GC event count reported alongside latency percentiles
- All hot-path code is zero-allocation

## Benchmark Results

Test environment: Apple M3 Max, 16 cores, .NET 8.0.6, Aeron C 1.51.0-SNAPSHOT, Workstation GC, IPC channel.

### Latency (Round-Trip Time)

#### `quick` profile (10K msg/s, 32-byte messages, 3 measurement iterations)

| Metric | P/Invoke | Aeron.NET |
|--------|----------|-----------|
| p50 | 166 ns | 166 ns |
| p90 | 209 ns | 250 ns |
| p99 | 1,375 ns | 6,335 ns |
| p99.9 | 7,751 ns | 8,671 ns |
| p99.99 | 12,215 ns | 11,839 ns |
| Max | 96,703 ns | 94,015 ns |
| Mean | 218 ns | 338 ns |
| StdDev | 737 ns | 1,072 ns |
| GC events | 0 | 701 |

#### `hft` profile (1M msg/s, 32-byte messages, 20 measurement iterations)

| Metric | P/Invoke | Aeron.NET |
|--------|----------|-----------|
| p50 | 167 ns | 167 ns |
| p90 | 208 ns | 209 ns |
| p99 | 500 ns | 7,875 ns |
| p99.9 | 5,167 ns | 15,879 ns |
| p99.99 | 13,255 ns | 144,255 ns |
| Max | 98,495 ns | 1,721,343 ns |
| Mean | 188 ns | 445 ns |
| StdDev | 377 ns | 9,678 ns |
| GC events | 0 | 3,918 |

#### `throughput` profile (500K msg/s, 288-byte messages, 10 measurement iterations)

| Metric | P/Invoke | Aeron.NET |
|--------|----------|-----------|
| p50 | 625 ns | 750 ns |
| p90 | 1,042 ns | 1,250 ns |
| p99 | 3,001 ns | 7,251 ns |
| p99.9 | 9,799 ns | 20,719 ns |
| p99.99 | 19,887 ns | 943,615 ns |
| Max | 63,295 ns | 1,607,679 ns |
| Mean | 772 ns | 1,321 ns |
| StdDev | 736 ns | 15,776 ns |
| GC events | 0 | 2,016 |

### Throughput (max msg/s)

#### 32-byte messages (exclusive publication, TryClaim)

| Profile | P/Invoke avg | Aeron.NET avg |
|---------|-------------|---------------|
| quick (3s) | 27.9M msg/s | 27.7M msg/s |
| hft (20s) | 26.9M msg/s | 30.5M msg/s |

#### 288-byte messages (exclusive publication, TryClaim)

| Profile | P/Invoke avg | Aeron.NET avg |
|---------|-------------|---------------|
| throughput (10s) | 44.9M msg/s | 29.1M msg/s |

Aeron.NET throughput with 288-byte messages degraded mid-test (47M -> 13M msg/s), with back-pressure rising from ~215/s to ~13M/s.

### Microbenchmarks (BenchmarkDotNet, InProcess, per-call)

#### P/Invoke Overhead

| Method | Mean | Allocated |
|--------|------|-----------|
| NanoClock (P/Invoke, SuppressGCTransition) | 13.5 ns | 0 B |
| NanoClock (Stopwatch.GetTimestamp) | 8.1 ns | 0 B |
| VersionFull (P/Invoke) | 3.1 ns | 0 B |
| ErrCode (P/Invoke) | 5.4 ns | 0 B |
| EpochClock (P/Invoke, SuppressGCTransition) | 14.6 ns | 0 B |

#### Offer / TryClaim / Poll (32-byte messages)

| Method | Mean | Allocated |
|--------|------|-----------|
| Native Offer | 2.3 ns | 0 B |
| Managed Offer | 1.5 ns | 0 B |
| Native TryClaim | 1.9 ns | 0 B |
| Managed TryClaim | 170.6 ns | 80 B |
| Native Poll | 7.5 ns | 0 B |
| Managed Poll | 10.3 ns | 24 B |

#### Offer / TryClaim / Poll (128-byte messages)

| Method | Mean | Allocated |
|--------|------|-----------|
| Native Offer | 2.3 ns | 0 B |
| Managed Offer | 1.4 ns | 0 B |
| Native TryClaim | 2.0 ns | 0 B |
| Managed TryClaim | 159.2 ns | 80 B |
| Native Poll | 7.5 ns | 0 B |
| Managed Poll | 10.6 ns | 24 B |

#### Offer / TryClaim / Poll (512-byte messages)

| Method | Mean | Allocated |
|--------|------|-----------|
| Native Offer | 2.3 ns | 0 B |
| Managed Offer | 1.5 ns | 0 B |
| Native TryClaim | 1.9 ns | 0 B |
| Managed TryClaim | 150.1 ns | 80 B |
| Native Poll | 7.6 ns | 0 B |
| Managed Poll | 11.0 ns | 24 B |

## Profiles

| Profile | Message Size | Rate | Batch | Use Case |
|---------|-------------|------|-------|----------|
| `quick` | 32 B | 10K/s | 1 | Smoke test |
| `hft` | 32 B | 1M/s | 1 | HFT order flow |
| `throughput` | 288 B | 500K/s | 10 | Market data |

## Prerequisites

1. **Build the Aeron C client:**
   ```bash
   cd aeron-c
   mkdir -p cppbuild/Release && cd cppbuild/Release
   cmake -DCMAKE_BUILD_TYPE=Release -DBUILD_AERON_DRIVER=ON ../..
   cmake --build . -j$(nproc)
   ```

2. **Set library path:**
   ```bash
   # macOS
   export DYLD_LIBRARY_PATH=$(pwd)/aeron-c/cppbuild/Release/lib

   # Linux
   export LD_LIBRARY_PATH=$(pwd)/aeron-c/cppbuild/Release/lib
   ```

3. **Start Aeron media driver:**
   ```bash
   # C driver
   ./aeron-c/cppbuild/Release/binaries/aeronmd

   # Java driver (alternative)
   java -cp aeron-c/aeron-all/build/libs/aeron-all-*.jar \
     io.aeron.driver.MediaDriver
   ```

## Usage

```bash
cd src

# Compare P/Invoke vs Aeron.NET
dotnet run -c Release --project Aeron.Benchmarks -- compare quick
dotnet run -c Release --project Aeron.Benchmarks -- compare hft
dotnet run -c Release --project Aeron.Benchmarks -- compare throughput

# Individual benchmarks
dotnet run -c Release --project Aeron.Benchmarks -- latency hft
dotnet run -c Release --project Aeron.Benchmarks -- throughput throughput

# BenchmarkDotNet microbenchmarks
dotnet run -c Release --project Aeron.Benchmarks -- micro

# Full suite
dotnet run -c Release --project Aeron.Benchmarks -- all hft
```

## Tuning

- **Server GC:** Add `<ServerGarbageCollection>true</ServerGarbageCollection>` to the benchmark csproj
- **CPU affinity:** Pin publisher/subscriber threads to dedicated cores (`taskset` on Linux)
- **NUMA:** Keep publisher and media driver on the same NUMA node
- **Exclusive publications:** Single-writer optimization (no CAS on offer)
- **TryClaim:** Zero-copy path avoids memcpy overhead
- **Fragment limit = 1:** Minimizes per-poll work for latency-sensitive subscribers
- **Disable Turbo Boost:** For consistent, jitter-free measurements
- **Isolate CPUs:** `isolcpus` kernel parameter to prevent OS scheduler interference
