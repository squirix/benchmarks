# Squirix Benchmarks

Reproducible end-to-end benchmarks for [Squirix](https://github.com/squirix/squirix), including comparisons with Garnet, Valkey, Redis and etcd.
The [results index](results/index.json) lists the published runs and the measurements selected for charts.

| Scenario | Method | Runs |
| --- | --- | --- |
| Single-node cache | [Method and commands](methodology/single-node.md) | [Squirix v0.1.0-preview.10](results/runs/single-node-v0.1.0-preview.10-2026-11-22-controlled/README.md) |
| Quorum cluster | [Method and commands](methodology/quorum.md) | [Squirix v0.1.0-preview.10](results/runs/quorum-v0.1.0-preview.10-2026-11-22-verified/README.md) |

The harnesses are in [single-node](single-node/) and [quorum](quorum/).
Each run keeps its configuration, measurements and reproduction details together.

## Build and validate

Install the .NET 10 SDK and Python 3.11 or newer. These are the same checks used by CI.

~~~powershell
dotnet build single-node/CacheLoad.csproj --configuration Release --warnaserror
dotnet build quorum/CacheQuorum.csproj --configuration Release --warnaserror
python scripts/validate-results.py
python -m unittest discover -s scripts
~~~

## Add a run

1. Run the [Single node](methodology/single-node.md) or [Quorum](methodology/quorum.md) workload in a clean output directory.
2. Fill in a run manifest with the tested rates, duration, versions, exact image digests, server package SHA-256 and host configuration.
3. Generate the measurements and index entry with the command below. Use `quorum` and `quorum/.local/measurements` for a quorum run.
4. Add the run report and the exact runner source archive to the output directory. Add the generated entry to the relevant scenario in `results/index.json`.
   Keep one listed run per Squirix release and scenario. Run the validation above before publishing.

~~~powershell
python scripts/aggregate-results.py cache --trials single-node/measurements --manifest run-manifest.json --output results/runs/my-cache-run
~~~

The command writes `measurements.json` and, when chart points qualify, an `index-entry.json` for publishing.
Use the shape below for `run-manifest.json`. Set the date, versions and hardware from the actual run. The `report` path is relative to `results/`.

~~~json
{
  "version": "v0.1.0-preview.10",
  "date": "2026-11-22",
  "series": "Single-node 95/5 · loopback TLS",
  "setup": "4 server vCPUs · 256-byte JSON values",
  "referenceLoad": 5000,
  "report": "runs/my-cache-run/",
  "systems": {"Squirix": "0.1.0-preview.10", "Garnet": "2.2.13", "Valkey": "9.2.0", "Redis": "8.10.3"},
  "testedRates": [2000, 5000, 8000, 20000],
  "secondsPerPoint": 6,
  "environment": {
    "cpu": "Actual CPU model",
    "guestCpu": 24,
    "engineVersion": "Docker Engine version",
    "kernel": "Guest kernel version",
    "dotnetRuntime": "Runtime version",
    "imageDigests": ["Registry image references with SHA-256 digests"],
    "squirixServerPackageSha256": "SHA-256 of the downloaded .nupkg"
  }
}
~~~

For Quorum use Squirix and etcd in `systems`, `[200, 400, 600, 1200]` for the tested rates and `5` for the duration.
Keep all tested rates in the manifest, including rates excluded from the chart. CI recomputes rate eligibility and p99 from the full trial matrix.

The index is a versioned JSON file with `schemaVersion`, `cache` and `quorum` at the top level. Every entry records the Squirix release tag,
measurement date, series name, a short setup description, reference load, report directory and points for each measured system.
Rates are in ops/s and p99 latencies are in milliseconds. Use a new series name when the hardware or measurement method changes.
Quorum entries use `systems` for writes and `readSystems` for reads. Release history uses the write reference point.

Code is available under the [Apache License 2.0](LICENSE).
