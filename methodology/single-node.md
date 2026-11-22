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
Run three repetitions with a different server order. Client latency starts at the scheduled arrival, so it includes scheduler lag and queueing.
The generator records GET and SET percentiles, GET latency from dispatch, scheduler lag, drops and completions within the offered window.

The chart shows the median of each repetition's GET p99. Include a load in the comparison only when every repetition for every system completes
at least 99.9% within the measurement window and the generator drops zero requests. Publish every tested load in the full run report.

## Run locally

The runner uses PowerShell and Docker with a Linux x86-64 engine. Its default CPU sets require 24 guest vCPUs.
Use a different series name if you change CPU placement, the runtime or workloads. The runner pins images by digest and starts containers on demand.

From the repository root, build the generator and fetch the published Squirix server package.

~~~powershell
Set-Location single-node
dotnet build CacheLoad.csproj --configuration Release
$package = "squirix.server.tool.0.1.0-preview.10.nupkg"
$url = "https://api.nuget.org/v3-flatcontainer/squirix.server.tool/0.1.0-preview.10/$package"
Invoke-WebRequest -Uri $url -OutFile $package
python -m zipfile -e $package server-package
dotnet bin/Release/net10.0/CacheLoad.dll certificates certs
./run-benchmark.ps1 -Pilot
./run-benchmark.ps1
~~~

The pilot checks TLS and the request path at two loads. The full run writes per-system, per-repetition JSONL under `single-node/measurements/`.
The script refuses to overwrite an existing measurement file. See the [first run report](../results/runs/single-node-v0.1.0-preview.10-2026-11-22/README.md) for the environment and results.
