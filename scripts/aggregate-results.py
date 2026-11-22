import argparse
import json
import re
from datetime import date
from pathlib import Path

from benchmark_results import load, require, summarize


def read_trials(directory):
    trials, health = [], []
    files = sorted(directory.glob("*.jsonl"))
    require(files, f"No completed JSONL files in {directory}")
    for file in files:
        for line in file.read_text(encoding="utf-8").splitlines():
            row = json.loads(line)
            (health if "Phase" in row else trials).append(row)
    return trials, health


def aggregate(scenario, manifest, directory):
    run = {key: manifest[key] for key in ("version", "date", "series", "setup", "referenceLoad", "report")}
    require(re.fullmatch(r"runs/[a-zA-Z0-9][a-zA-Z0-9.-]*/", run["report"]), "Invalid report directory")
    date.fromisoformat(run["date"])
    require(run["version"] == f"v{manifest['systems']['Squirix']}", "Release tag does not match measured Squirix")
    require(isinstance(run["series"], str) and run["series"] and isinstance(run["setup"], str) and run["setup"],
            "Missing series or test setup")
    require(type(run["referenceLoad"]) is int and run["referenceLoad"] > 0, "Invalid reference load preference")
    measured = {key: manifest[key] for key in ("environment", "systems", "testedRates", "secondsPerPoint")}
    require(isinstance(measured["environment"], dict) and measured["environment"], "Missing environment metadata")
    trials, health = read_trials(directory)
    measured["trials"] = trials
    if scenario == "quorum":
        measured["clusterHealth"] = health
    else:
        require(not health, "Unexpected cluster snapshots in a Single node run")
    accepted, points = summarize(scenario, measured)
    measured["acceptedChartRates"] = accepted
    rates = accepted if scenario == "cache" else accepted["write"]
    if not rates or (scenario == "quorum" and not accepted["read"]):
        return measured, None
    preferred = run["referenceLoad"]
    run["referenceLoad"] = max((rate for rate in rates if rate <= preferred), default=rates[0])
    run["systems"] = points["mixed" if scenario == "cache" else "write"]
    if scenario == "quorum":
        run["readSystems"] = points["read"]
    return measured, run


def write_json(path, data):
    with path.open("x", encoding="utf-8", newline="\n") as output:
        json.dump(data, output, indent=2, ensure_ascii=False)
        output.write("\n")


def main():
    parser = argparse.ArgumentParser(description="Calculate chart results from a complete benchmark run.")
    parser.add_argument("scenario", choices=("cache", "quorum"))
    parser.add_argument("--trials", required=True, type=Path)
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    measured, run = aggregate(args.scenario, load(args.manifest), args.trials)
    args.output.mkdir(parents=True, exist_ok=True)
    for name in ("measurements.json", "index-entry.json"):
        require(not (args.output / name).exists(), f"Output already exists: {args.output / name}")
    write_json(args.output / "measurements.json", measured)
    if run:
        write_json(args.output / "index-entry.json", run)
        print(f"Chart rates: {measured['acceptedChartRates']}")
    else:
        print("No comparable chart points. Measurements saved without an index entry.")


if __name__ == "__main__":
    main()
