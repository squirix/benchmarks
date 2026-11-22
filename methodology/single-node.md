# Single-node cache benchmark

This benchmark measures end-to-end GET and SET latency at fixed offered request rates. The first series uses one in-memory node per system, TLS,
16,384 uniformly sampled keys, 256-byte JSON values and a 95% GET / 5% SET mix.

## Stand and workload

- Launch one server at a time. For the first series, the server was pinned to guest CPUs 12–15 with a 4 GiB limit.
  The load client used guest CPUs 16–23 with a 3 GiB limit. The containers shared the server’s loopback network namespace.
- Preload all keys. Use 48 persistent TLS sessions with one outstanding operation per session. Validate returned values and write acknowledgments.
- Use the published Squirix SDK for Squirix. The first run used gRPC. The same minimal RESP2 adapter drives Garnet, Valkey and Redis.
- Use the same test certificate, offered rates and deterministic request sequence for each system in a repetition. Disable persistence.
  Record the image digests and any server options.

## Measurement

After preloading, warm up for five seconds. Each rate adds a two-second warmup and a six-second measurement.
When scheduler p99 exceeds 0.5 ms, repeat the window up to twice with the same request seed and warmup.
Each trial records earlier windows and uses the first scheduler-qualified window, or the third window if all are late.
Run three repetitions with a different server order. Client latency starts at the scheduled arrival, so it includes scheduler lag and queueing.
The generator records GET and SET percentiles, GET latency from dispatch, scheduler lag, drops and completions within the offered window.

The chart shows the median of each repetition's GET p99. A rate qualifies when every repetition for every system completes at least
99.9% within the measurement window, drops zero requests and has scheduler p99 at or below 0.5 ms. The run report retains every attempted rate.
The scheduler limit bounds the effect of late request dispatch on the latency comparison. Server latency and completions stay part of validation
and do not trigger retries.

## Run locally

The runner uses PowerShell 7 and Docker with a Linux x86-64 engine. Its default CPU sets require 24 guest vCPUs.
Use a different series name if you change CPU placement, the runtime or workloads. The runner pins images by digest and starts containers on demand.

From the repository root, build the generator and fetch the published Squirix server package.

~~~powershell
Set-Location single-node
dotnet build CacheLoad.csproj --configuration Release
$package = "squirix.server.tool.0.1.0-preview.10.nupkg"
$url = "https://api.nuget.org/v3-flatcontainer/squirix.server.tool/0.1.0-preview.10/$package"
Invoke-WebRequest -Uri $url -OutFile $package
$expected = '9a124929a6147a93944917620fbf15907cd1ff7b02e473b08f942a238872b405'
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ine $expected) { throw 'Unexpected Squirix package checksum' }
python -m zipfile -e $package server-package
dotnet bin/Release/net10.0/CacheLoad.dll certificates certs
./run-benchmark.ps1 -Pilot
./run-benchmark.ps1
~~~

The pilot checks TLS and the request path at two loads. The full run writes per-system, per-repetition JSONL under `single-node/measurements/`.
The runner keeps completed repetitions when resuming. Incomplete output stays in `.partial` files. Use a clean output directory for a new series.
See [Add a run](../README.md#add-a-run) for the aggregation and publication commands.
