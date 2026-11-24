# Quorum cluster · Squirix v0.2.0-preview.1

Measured on 24 November 2026. The [per-trial data](measurements.json) retain all 48 points.
The [source archive](benchmark-source.zip) and [run manifest](manifest.json) record the inputs for this run.

## Writes

Median of three SET or PUT p99 values in milliseconds.

| System | 200 ops/s | 400 ops/s | 600 ops/s | 1,200 ops/s |
| --- | --- | --- | --- | --- |
| Squirix 0.2.0-preview.1 | 8.3300 | 8.4125 | 7.8588 | 10.2888 |
| etcd 3.7.4 | 3.2233 | 2.9838 | 2.7030 | 3.2481 |

## Reads

Median of three quorum GET or linearizable Range p99 values in milliseconds.

| System | 200 ops/s | 400 ops/s | 600 ops/s | 1,200 ops/s |
| --- | --- | --- | --- | --- |
| Squirix 0.2.0-preview.1 | 0.9297 | 0.7646 | 0.7218 | 0.5424 |
| etcd 3.7.4 | 0.9109 | 0.7623 | 0.6745 | 0.6639 |

## Test setup

Three persistent nodes per system, mutual TLS between peers and 1,024 preloaded keys. The data retain 12 before/after cluster snapshots.
Squirix uses its binary protocol and bearer authentication. etcd uses gRPC with server TLS and application authentication disabled.
Clients use loopback TLS with 48 sessions and 256-byte JSON values. Each server node uses four pinned vCPUs and 4 GiB.
The client uses eight separate vCPUs and 3 GiB on a shared Docker Desktop VM.

See the [benchmark methodology](../../../methodology/quorum.md) for warmup, eligibility and replay.
