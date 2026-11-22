import json
import math
import re
from datetime import date
from pathlib import Path
from statistics import median


ROOT = Path(__file__).resolve().parents[1] / "results"
REPORT = re.compile(r"runs/[a-zA-Z0-9][a-zA-Z0-9.-]*/")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def verify_points(systems, measured, report, workload, reference=None):
    names = [system["name"] for system in systems]
    require(len(names) >= 2 and len(names) == len(set(names)) and "Squirix" in names, f"Invalid systems in {report}")
    cache = workload == "mixed"
    offered_loads = measured["acceptedChartRates"] if cache else measured["acceptedChartRates"][workload]
    require(reference is None or reference in offered_loads, f"Missing Squirix reference load in {report}")
    percentile = "Write" if workload == "write" else "Read"
    threshold = 999 if cache else 990
    for system in systems:
        require(bool(system["version"]), f"Missing system version in {report}")
        points = system["points"]
        loads = [point["offered"] for point in points]
        require(bool(loads) and loads == sorted(set(loads)), f"Unsorted or duplicate rates in {report}")
        require(loads == offered_loads, f"Chart rates do not match measurements for {system['name']} in {report}")
        for point in points:
            load_rate, latency = point["offered"], point["p99"]
            require(type(load_rate) is int and load_rate > 0, f"Invalid offered load in {report}")
            require(type(latency) in (int, float) and math.isfinite(latency) and latency >= 0, f"Invalid p99 in {report}")
            trials = [trial for trial in measured["trials"] if trial["System"] == system["name"]
                      and trial["Result"]["OfferedOpsPerSecond"] == load_rate and (cache or trial["Workload"] == workload)]
            require(bool(trials), f"Missing trials for {system['name']} at {load_rate} in {report}")
            if not cache:
                require(len(trials) == 3 and {trial["Repetition"] for trial in trials} == {1, 2, 3},
                        f"Missing or duplicate trials for {system['name']} at {load_rate} in {report}")
            samples = [trial["Result"] for trial in trials]
            require(all(sample["Dropped"] == 0 and sample["CompletedWithinWindow"] * 1000 >= sample["Offered"] * threshold
                        for sample in samples), f"Unsustained chart point for {system['name']} at {load_rate} in {report}")
            require(abs(median(sample[percentile]["P99Ms"] for sample in samples) - latency) <= 1e-6,
                    f"p99 does not match trials for {system['name']} at {load_rate} in {report}")


def main():
    index = load(ROOT / "index.json")
    require(index["schemaVersion"] == 1, "Unsupported result index schema")
    count = 0
    for scenario in ("cache", "quorum"):
        versions = set()
        for run in index[scenario]:
            version = run["version"]
            require(re.fullmatch(r"v\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", version), f"Invalid release tag: {version}")
            require(version not in versions, f"Duplicate {scenario} result for {version}")
            versions.add(version)
            date.fromisoformat(run["date"])
            require(bool(run["series"] and run["setup"]), f"Missing run metadata for {version}")
            require(REPORT.fullmatch(run["report"]), f"Invalid report path for {version}")
            report = ROOT / run["report"]
            require((report / "README.md").is_file(), f"Missing report for {version}")
            measured = load(report / "measurements.json")
            reference = run["referenceLoad"]
            require(type(reference) is int and reference > 0, f"Invalid reference load in {report}")
            verify_points(run["systems"], measured, report, "mixed" if scenario == "cache" else "write", reference)
            if scenario == "quorum":
                verify_points(run["readSystems"], measured, report, "read")
            count += 1
    print(f"Validated {count} benchmark runs.")


if __name__ == "__main__":
    main()
