# Single-node cache · Squirix v0.1.0-preview.10

Measured on 22 November 2026. The [per-trial data](measurements.json) contain all 48 points.
Use the [source archive](benchmark-source.zip) and [run manifest](manifest.json) to reproduce this series.

## Reads

Median of three trial GET p99 values in milliseconds.

| System | 2,000 ops/s | 5,000 ops/s | 8,000 ops/s |
| --- | ---: | ---: | ---: |
| Squirix 0.1.0-preview.10 | 0.4534 | 0.5901 | 0.9443 |
| Garnet 2.2.13 | 0.3551 | 0.2998 | 0.3234 |
| Valkey 9.2.0 | 0.3399 | 0.2586 | 0.2525 |
| Redis 8.10.3 | 0.3439 | 0.2596 | 0.2694 |

## Test setup

- Intel(R) Core(TM) Ultra 9 285K with 24 guest vCPUs in a shared Linux x86-64 Docker Desktop VM.
  WSL2 kernel 6.18.58.1-microsoft-standard-WSL2 and Docker Engine 29.9.3.
- One server deployment at a time, with 4 vCPUs and 4 GiB per node. Other host services stayed running.
- Client TLS on the first node's loopback interface. The client used 8 separate vCPUs, a 3 GiB limit and 48 sessions.
- In-memory workload with 16,384 preloaded keys, 256-byte JSON values and a 95% GET / 5% SET mix.
- Squirix uses its .NET SDK over gRPC. Garnet, Valkey and Redis use the same RESP2 adapter.

Scheduler p99 exceeded 0.5 ms in 7 earlier windows. The data retain each attempted window.

### Rates excluded from the charts

| System | Offered ops/s | Repetition | Completed in window | Drops | Scheduler p99 (ms) |
| --- | ---: | ---: | ---: | ---: | ---: |
| Squirix | 20,000 | 1 | 99.998% | 0 | 0.735 |
| Squirix | 20,000 | 2 | 99.999% | 0 | 0.776 |
| Squirix | 20,000 | 3 | 99.999% | 0 | 0.730 |

See the [method and commands](../../../methodology/single-node.md) for warmup, rejection rules and replay.
