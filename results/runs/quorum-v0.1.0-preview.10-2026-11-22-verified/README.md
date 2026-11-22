# Quorum cluster · Squirix v0.1.0-preview.10

Measured on 22 November 2026. The [per-trial data](measurements.json) contain all 48 points.
Use the [source archive](benchmark-source.zip) and [run manifest](manifest.json) to reproduce this series.

## Writes

Median of three SET or PUT p99 values in milliseconds.

| System | 200 ops/s | 400 ops/s | 600 ops/s | 1,200 ops/s |
| --- | ---: | ---: | ---: | ---: |
| Squirix 0.1.0-preview.10 | 9.1599 | 10.5923 | 10.4797 | 11.2328 |
| etcd 3.7.4 | 6.2334 | 2.7665 | 7.6318 | 4.4667 |

## Reads

Median of three quorum GET or linearizable Range p99 values in milliseconds.

| System | 200 ops/s | 400 ops/s | 600 ops/s | 1,200 ops/s |
| --- | ---: | ---: | ---: | ---: |
| Squirix 0.1.0-preview.10 | 1.2547 | 1.0436 | 0.9640 | 0.9695 |
| etcd 3.7.4 | 0.9168 | 1.0145 | 0.7326 | 0.6998 |

## Test setup

- Intel(R) Core(TM) Ultra 9 285K with 24 guest vCPUs in a shared Linux x86-64 Docker Desktop VM.
  WSL2 kernel 6.18.58.1-microsoft-standard-WSL2 and Docker Engine 29.9.3.
- One server deployment at a time, with 4 vCPUs and 4 GiB per node. Other host services stayed running.
- Client TLS on the first node's loopback interface. The client used 8 separate vCPUs, a 3 GiB limit and 48 sessions.
- Three persistent nodes per cluster, mutual TLS between peers, 1,024 preloaded keys and 256-byte JSON values.
- The Squirix client uses bearer authentication. The etcd client uses TLS with application authentication disabled.

The data include 12 cluster health snapshots across both systems, with one snapshot before and one after each trial.
At 600 ops/s, individual etcd write p99 values ranged from 3.05 to 39.70 ms on this shared host.

See the [method and commands](../../../methodology/quorum.md) for warmup, rejection rules and replay.
