import math
import re
from datetime import date
from pathlib import Path

from benchmark_results import load, require, summarize


ROOT = Path(__file__).resolve().parents[1] / "results"
REPORT = re.compile(r"runs/[a-zA-Z0-9][a-zA-Z0-9.-]*/")


def verify_points(published, calculated, context):
    published_names = [system["name"] for system in published]
    require(sorted(published_names) == sorted(system["name"] for system in calculated),
            f"Published systems differ from measurements in {context}")
    by_system = {system["name"]: system for system in published}
    for system in calculated:
        item = by_system[system["name"]]
        require(item["version"] == system["version"], f"Wrong version for {system['name']} in {context}")
        require([point["offered"] for point in item["points"]] == [point["offered"] for point in system["points"]],
                f"Wrong chart rates for {system['name']} in {context}")
        for point, expected in zip(item["points"], system["points"], strict=True):
            require(type(point["offered"]) is int and type(point["p99"]) in (int, float)
                    and math.isfinite(point["p99"]) and abs(point["p99"] - expected["p99"]) <= 1e-6,
                    f"Wrong p99 for {system['name']} at {point['offered']} in {context}")


def verify_run(scenario, run, measured):
    report = run["report"]
    accepted, points = summarize(scenario, measured)
    require(measured["acceptedChartRates"] == accepted, f"Chart rates fail the run policy in {report}")
    writes = accepted if scenario == "cache" else accepted["write"]
    reference = run["referenceLoad"]
    require(type(reference) is int and reference in writes, f"Invalid reference load in {report}")
    primary = "mixed" if scenario == "cache" else "write"
    version = next(system["version"] for system in points[primary] if system["name"] == "Squirix")
    require(run["version"] == f"v{version}", f"Release tag does not match measured Squirix version in {report}")
    verify_points(run["systems"], points[primary], report)
    if scenario == "quorum":
        require(accepted["read"], f"No comparable read points in {report}")
        verify_points(run["readSystems"], points["read"], report)


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
            require((report / "benchmark-source.zip").is_file(), f"Missing source archive for {version}")
            measured = load(report / "measurements.json")
            manifest = load(report / "manifest.json")
            for key in ("testedRates", "secondsPerPoint", "systems", "environment"):
                require(manifest[key] == measured[key], f"Run manifest and measurements disagree on {key} in {report}")
            for key in ("version", "date", "report", "series", "setup"):
                require(manifest[key] == run[key], f"Run manifest and index disagree on {key} in {report}")
            verify_run(scenario, run, measured)
            count += 1
    print(f"Validated {count} benchmark runs.")


if __name__ == "__main__":
    main()
