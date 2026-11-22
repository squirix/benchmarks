# Single-node cache · Squirix v0.1.0-preview.10

Measured on 22 November 2026. The [per-trial measurements](measurements.json) include all 48 points.
The [source archive](benchmark-source.zip) contains the exact generator and configuration used for this run.

## Chart results

These are the medians of three trial GET p99 values in milliseconds.

| System | 2,000 ops/s | 5,000 ops/s | 8,000 ops/s |
| --- | ---: | ---: | ---: |
| Squirix 0.1.0-preview.10 | 0.5872 | 0.8342 | 1.4424 |
| Garnet 2.2.13 | 11.3431 | 0.3547 | 0.3899 |
| Valkey 9.2.0 | 0.3391 | 0.3367 | 0.3076 |
| Redis 8.10.3 | 0.3637 | 23.0925 | 30.7780 |

At 20,000 ops/s, the generator dropped 10,567 operations during the third Squirix trial. The full data includes this load.
Scheduler p99 reached 14.924 ms in one trial. This local series captures the client, transport and server on a shared host, including host scheduling effects.

## Environment

- Intel Core Ultra 9 285K with a 24-vCPU Linux x86-64 Docker Desktop VM and WSL2 kernel 6.18.58.1. Docker Engine 29.9.3.
- Servers ran sequentially on guest CPUs 12–15 with a 4 GiB limit. The load container used CPUs 16–23 with a 3 GiB limit.
  Other services on the host stayed running.
- In-memory storage, loopback TLS, 48 persistent sessions, 16,384 preloaded keys and 256-byte JSON values.
- The workload was 95% GET and 5% SET. Trials used 2,000, 5,000, 8,000 and 20,000 offered ops/s.
- Squirix used its .NET SDK over gRPC. Garnet, Valkey and Redis used the same RESP2 adapter.
  Garnet used a 512 MiB log, a 16 MiB index and disabled object types. Valkey and Redis used their default thread settings.

See the [method and commands](../../../methodology/single-node.md) for warmup, window timing and the chart inclusion rule.
The measurements JSON records image digests, the .NET runtime and the Squirix server package SHA-256.
