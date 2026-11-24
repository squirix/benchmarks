# Single-node cache · Squirix v0.2.0-preview.1

Measured on 24 November 2026. The [per-trial data](measurements.json) retain all 48 points.
The [source archive](benchmark-source.zip) and [run manifest](manifest.json) record the inputs for this run.

## Reads

Median of three GET p99 values in milliseconds.

| System | 2,000 ops/s | 5,000 ops/s | 8,000 ops/s |
| --- | --- | --- | --- |
| Squirix 0.2.0-preview.1 | 0.3702 | 0.3598 | 0.3793 |
| Garnet 2.2.13 | 0.3113 | 0.2880 | 0.3146 |
| Valkey 9.2.0 | 0.2770 | 0.2649 | 0.2325 |
| Redis 8.10.3 | 0.2575 | 0.2162 | 0.2172 |

## Test setup

One in-memory node per system, 16,384 preloaded keys and a 95% GET / 5% SET workload.
Squirix uses the published SDK and its binary protocol. Garnet, Valkey and Redis use the same RESP2 client.
Clients use loopback TLS with 48 sessions and 256-byte JSON values. Each server node uses four pinned vCPUs and 4 GiB.
The client uses eight separate vCPUs and 3 GiB on a shared Docker Desktop VM.

## Rates excluded from the charts

| System | Workload | Offered ops/s | Repetition | Completed in window | Drops | Scheduler p99 (ms) |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Squirix | mixed | 20,000 | 2 | 99.998% | 0 | 0.951 |
| Squirix | mixed | 20,000 | 3 | 99.998% | 0 | 0.571 |

See the [benchmark methodology](../../../methodology/single-node.md) for warmup, eligibility and replay.
