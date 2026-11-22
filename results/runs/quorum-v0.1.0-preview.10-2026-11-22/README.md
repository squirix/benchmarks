# Quorum cluster · Squirix v0.1.0-preview.10

Measured on 22 November 2026. The [per-trial measurements](measurements.json) contain all 48 points.
The [source archive](benchmark-source.zip) contains the exact runner and generator used for this series.

## Writes

Median of three SET or PUT p99 values, in milliseconds.

| System | 200 ops/s | 400 ops/s | 600 ops/s | 1,200 ops/s |
| --- | ---: | ---: | ---: | ---: |
| Squirix 0.1.0-preview.10 | 9.4340 | 13.8925 | 12.8973 | 13.3306 |
| etcd 3.7.4 | 3.0788 | 3.8499 | 4.9100 | 5.0478 |

## Reads

Median of three quorum GET or linearizable Range p99 values, in milliseconds.

| System | 200 ops/s | 400 ops/s | 600 ops/s | 1,200 ops/s |
| --- | ---: | ---: | ---: | ---: |
| Squirix 0.1.0-preview.10 | 1.7193 | 1.5339 | 1.0688 | 1.2926 |
| etcd 3.7.4 | 1.1375 | 0.8912 | 0.7649 | 0.7571 |

## Run conditions

- Intel(R) Core(TM) Ultra 9 285K with 24 guest vCPUs in a shared Linux x86-64 Docker Desktop VM.
  WSL2 kernel 6.18.58.1-microsoft-standard-WSL2 and Docker Engine 29.9.3.
- One cluster at a time, three nodes, 4 vCPUs and 4 GiB per node, with fresh Docker volumes.
  The load container used 8 separate vCPUs and a 3 GiB limit. Other host services stayed running.
- Client TLS on the first node's loopback interface, mutual TLS between peers, 48 persistent gRPC sessions and
  1,024 preloaded keys with 256-byte JSON values.
- The Squirix SDK uses bearer authentication. The etcd gRPC client uses server TLS with application authentication disabled.

The generator tested 200, 400, 600 and 1,200 offered ops/s in separate write-only and read-only workloads.
Rates qualify for the chart when both systems complete at least 99% within the window and drop zero requests in every trial.
All four rates qualified for both workloads. One etcd write trial at 400 ops/s recorded a p99 of 31.986 ms.

Scheduler p99 reached 0.180 ms in one trial. The results include the client, transport, storage and scheduling effects
of this shared host. The [method and commands](../../../methodology/quorum.md) describe warmup, replication, and how to reproduce the run.
