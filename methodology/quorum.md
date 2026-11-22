# Quorum cluster benchmark

Each system runs as a three-node persistent cluster with TLS to the client and mutual TLS between peers.
One write workload measures Squirix SET and etcd PUT. A separate read workload measures Squirix quorum GET and etcd linearizable Range.

## Stand and workload

- Run one cluster at a time with fresh Docker volumes. Pin the three nodes to guest CPUs 0–3, 4–7 and 8–11, with 4 GiB per node.
  Pin the load generator to CPUs 16–23 with a 3 GiB limit.
- Send measured requests over TLS on the first node's loopback interface. Select Squirix keys whose primary group is on the first node.
- Check all three Squirix nodes before and after each trial. Node A must report copies B and C as ready for group A.
  Elect etcd node A and verify all three members agree on its leadership and term before and after the trial.
- Preload 1,024 keys with 256-byte JSON values. Use 48 persistent gRPC sessions with one in-flight request per session.
  Squirix uses its published SDK and bearer authentication. etcd uses its gRPC KV API with server TLS and application authentication disabled.
- Squirix uses replication factor three, persistence and quorum reads. etcd uses its default durable Raft path and linearizable reads.

## Measurement

Within each repetition, run the write workload first and the read workload second. Warm up for three seconds before each workload.
Each offered rate then has a two-second warmup and a five-second measurement. Alternate system order over three repetitions.
Use the same key sequence at each rate within a repetition for both systems.

Latency starts at the scheduled arrival and includes queueing. Each trial records operation p50, p95 and p99, dispatch-to-response
latency, scheduler lag, queue drops and requests completed within the measurement window. An RPC or validation error fails the trial.

For each workload, a rate is chartable when every repetition for both systems drops zero requests and completes at least
99% of offered requests within the measurement window. The chart shows the median of the three trial p99 values.
The report retains all tested rates and the before/after cluster snapshots. The release-history reference is 600 ops/s,
with the nearest lower chartable write rate used if available.

## Run locally

Use the .NET 10 SDK, PowerShell 7, Python 3 and a Linux x86-64 Docker engine with at least 24 guest vCPUs.
Use a fresh checkout or move existing run output out of the way before starting a new series.
Run these commands from the repository root.

~~~powershell
dotnet build quorum/CacheQuorum.csproj --configuration Release
$package = "single-node/squirix.server.tool.0.1.0-preview.10.nupkg"
$url = "https://api.nuget.org/v3-flatcontainer/squirix.server.tool/0.1.0-preview.10/squirix.server.tool.0.1.0-preview.10.nupkg"
Invoke-WebRequest -Uri $url -OutFile $package
$expected = '9a124929a6147a93944917620fbf15907cd1ff7b02e473b08f942a238872b405'
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ine $expected) { throw 'Unexpected Squirix package checksum' }
python -m zipfile -e $package single-node/server-package
dotnet quorum/bin/Release/net10.0/CacheQuorum.dll prepare quorum/.local
./quorum/run-benchmark.ps1 -Pilot
./quorum/run-benchmark.ps1
~~~

The pilot checks the request path at two loads. The full series tests 200, 400, 600 and 1,200 ops/s.
The runner writes separate JSONL files under `quorum/.local/measurements/`. It keeps completed repetitions when resuming.
The ignored `quorum/.local/` directory also contains temporary TLS and JWT credentials.
See [Add a run](../README.md#add-a-run) for the aggregation and publication commands.
