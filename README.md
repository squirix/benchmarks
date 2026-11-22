# Squirix Benchmarks

Reproducible end-to-end benchmarks for [Squirix](https://github.com/squirix/squirix), including comparisons with Garnet, Valkey, Redis and etcd.
The [benchmark page](https://squirix.io/performance/) reads the public [results index](results/index.json).

| Scenario | Method | Runs |
| --- | --- | --- |
| Single-node cache | [Method and commands](methodology/single-node.md) | [Squirix v0.1.0-preview.10](results/runs/single-node-v0.1.0-preview.10-2026-11-22/README.md) |
| Quorum cluster | [Method and commands](methodology/quorum.md) | [Squirix v0.1.0-preview.10](results/runs/quorum-v0.1.0-preview.10-2026-11-22/README.md) |

The harnesses are in [single-node](single-node/) and [quorum](quorum/).
Each run keeps its configuration, measurements and reproduction details together.

## Build and validate

Install the .NET 10 SDK and Python 3.11 or newer. These are the same checks used by CI.

~~~powershell
dotnet build single-node/CacheLoad.csproj --configuration Release --warnaserror
dotnet build quorum/CacheQuorum.csproj --configuration Release --warnaserror
python scripts/validate-results.py
~~~

## Add a run

1. Add a new directory under `results/runs/` with its report and measurements. Record the harness revision, server versions, image digests, host and exact configuration.
2. Append an entry to the relevant scenario in `results/index.json`. Retain old entries and their links.
3. Run the build and results validation before opening a pull request.

The index is a versioned JSON file with `schemaVersion`, `cache` and `quorum` at the top level. Every entry records the Squirix release tag,
measurement date, series name, a short setup description, reference load, report directory and points for each measured system.
Rates are in ops/s and p99 latencies are in milliseconds. Use a new series name when the hardware or measurement method changes.
Quorum entries use `systems` for writes and `readSystems` for reads. Release history uses the write reference point.

Code is available under the [Apache License 2.0](LICENSE).
