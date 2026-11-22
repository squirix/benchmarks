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


def verify_points(run, report, scenario):
    systems = run["systems"]
    reference = run["referenceLoad"]
    require(type(reference) is int and reference > 0, f"Invalid reference load in {report}")
    names = [system["name"] for system in systems]
    require(len(names) >= 2 and len(names) == len(set(names)) and "Squirix" in names, f"Invalid systems in {report}")
    measured = load(report / "measurements.json")
    offered_loads = [point["offered"] for point in systems[names.index("Squirix")]["points"]]
    require(reference in offered_loads, f"Missing Squirix reference load in {report}")
    if scenario == "cache":
        require(offered_loads == measured["acceptedChartRates"], f"Chart rates do not match measurements in {report}")
    for system in systems:
        require(bool(system["version"]), f"Missing system version in {report}")
        points = system["points"]
        loads = [point["offered"] for point in points]
        require(bool(loads) and loads == sorted(set(loads)), f"Unsorted or duplicate rates in {report}")
        for point in points:
            load_rate, latency = point["offered"], point["p99"]
            require(type(load_rate) is int and load_rate > 0, f"Invalid offered load in {report}")
            require(type(latency) in (int, float) and math.isfinite(latency) and latency >= 0, f"Invalid p99 in {report}")
            if scenario != "cache":
                continue
            trials = [trial["Result"] for trial in measured["trials"]
                      if trial["System"] == system["name"] and trial["Result"]["OfferedOpsPerSecond"] == load_rate]
            require(bool(trials), f"Missing trials for {system['name']} at {load_rate} in {report}")
            require(all(trial["Dropped"] == 0 and trial["CompletedWithinWindow"] * 1000 >= trial["Offered"] * 999
                        for trial in trials), f"Unsustained chart point for {system['name']} at {load_rate} in {report}")
            require(abs(median(trial["Read"]["P99Ms"] for trial in trials) - latency) <= 1e-6,
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
            verify_points(run, report, scenario)
            count += 1
    print(f"Validated {count} benchmark runs.")


if __name__ == "__main__":
    main()
