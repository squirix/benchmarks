import json
import math
from itertools import product
from statistics import median


REPETITIONS = (1, 2, 3)
MAX_CACHE_SCHEDULER_P99_MS = 0.5
CACHE_SYSTEMS = {"Squirix", "Garnet", "Valkey", "Redis"}
QUORUM_SYSTEMS = {"Squirix", "etcd"}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def is_count(value):
    return type(value) is int and value >= 0


def checked_distribution(distribution, context):
    count = distribution["Count"]
    percentiles = [distribution[f"P{percentile}Ms"] for percentile in (50, 95, 99)]
    require(is_count(count), f"Invalid sample count in {context}")
    require(all(type(value) in (int, float) and math.isfinite(value) and value >= 0 for value in percentiles)
            and percentiles == sorted(percentiles), f"Invalid percentile in {context}")
    require(count > 0 or percentiles == [0, 0, 0], f"Percentiles without samples in {context}")
    return count


def checked_samples(result, rate, seconds, workload, context):
    offered, dropped, completed = (result[key] for key in ("Offered", "Dropped", "CompletedWithinWindow"))
    require(result["Seconds"] == seconds and type(result["Seconds"]) is int
            and is_count(offered) and offered == rate * seconds
            and is_count(dropped) and is_count(completed) and completed <= offered - dropped,
            f"Inconsistent operation counters in {context}")
    reads = checked_distribution(result["Read"], context)
    writes = checked_distribution(result["Write"], context)
    require(reads + writes == offered - dropped, f"Missing completed operation samples in {context}")
    require(checked_distribution(result["ReadService"], context) == reads
            and checked_distribution(result["SchedulerLag"], context) == offered,
            f"Missing scheduler or service samples in {context}")
    if "WriteService" in result:
        require(checked_distribution(result["WriteService"], context) == writes,
                f"Missing write service samples in {context}")
    if workload == "mixed":
        expected_writes = (offered + 19) // 20
        require(writes <= expected_writes and reads <= offered - expected_writes, f"Unexpected mix in {context}")
    else:
        require((reads if workload == "write" else writes) == 0, f"Unexpected operation in {context}")


def eligible(result, scenario):
    threshold = 999 if scenario == "cache" else 990
    completed = result["CompletedWithinWindow"] * 1000 >= result["Offered"] * threshold
    lag_ok = scenario != "cache" or result["SchedulerLag"]["P99Ms"] <= MAX_CACHE_SCHEDULER_P99_MS
    return result["Dropped"] == 0 and completed and lag_ok


def verify_health(evidence):
    expected = set(product(QUORUM_SYSTEMS, REPETITIONS, ("before", "after")))
    groups = {}
    for row in evidence:
        key = row["System"], row["Repetition"], row["Phase"]
        require(type(key[1]) is int and key in expected and key not in groups, f"Unexpected cluster snapshot {key}")
        nodes = row["Nodes"]
        require(len(nodes) == 3, f"Missing cluster members in {key}")
        if key[0] == "Squirix":
            members = {node["NodeId"]: node["Copies"] for node in nodes}
            require(set(members) == {"A", "B", "C"} and not members["B"] and not members["C"],
                    f"Unexpected Squirix group A leader in {key}")
            copies = members["A"]
            require(len(copies) == 2 and {copy["NodeId"] for copy in copies} == {"B", "C"}
                    and all(copy["State"] == "ready" and is_count(copy["LagEntries"]) for copy in copies),
                    f"Squirix group A copies must both be ready in {key}")
            groups[key] = None
        else:
            endpoints = {node["Endpoint"]: node for node in nodes}
            require(set(endpoints) == {f"https://node-{node}:2379" for node in "abc"}, f"Unexpected etcd members in {key}")
            leader = endpoints["https://node-a:2379"]
            require(len({node["Id"] for node in nodes}) == 3 and leader["Id"]
                    and is_count(leader["Term"]) and leader["Term"] > 0
                    and all(node["Leader"] == leader["Id"] and node["Term"] == leader["Term"] for node in nodes),
                    f"etcd members disagree about leadership in {key}")
            groups[key] = leader["Id"], leader["Term"]
    require(set(groups) == expected, "Missing before/after quorum snapshots")
    for repetition in REPETITIONS:
        require(groups["etcd", repetition, "before"] == groups["etcd", repetition, "after"],
                f"etcd leadership changed during repetition {repetition}")


def summarize(scenario, measured):
    cache = scenario == "cache"
    versions = measured["systems"]
    require(isinstance(versions, dict) and set(versions) == (CACHE_SYSTEMS if cache else QUORUM_SYSTEMS)
            and all(isinstance(version, str) and version for version in versions.values()), "Invalid measured systems")
    rates = measured["testedRates"]
    require(isinstance(rates, list) and rates and all(type(rate) is int and rate > 0 for rate in rates)
            and rates == sorted(set(rates)), "Invalid tested rates")
    seconds = measured["secondsPerPoint"]
    require(type(seconds) is int and seconds > 0, "Invalid measurement duration")
    workloads = ("mixed",) if cache else ("write", "read")
    expected = set(product(versions, REPETITIONS, workloads, rates))
    trials = {}
    for trial in measured["trials"]:
        result = trial["Result"]
        workload = trial.get("Workload", "mixed") if cache else trial["Workload"]
        key = trial["System"], trial["Repetition"], workload, result["OfferedOpsPerSecond"]
        require(type(key[1]) is int and type(key[3]) is int and key in expected and key not in trials,
                f"Invalid or duplicate trial {key}")
        checked_samples(result, key[3], seconds, workload, key)
        if cache:
            earlier_windows = trial["EarlierWindows"]
            require(isinstance(earlier_windows, list) and len(earlier_windows) <= 2, f"Too many scheduler retries in {key}")
            for earlier in earlier_windows:
                require(earlier["OfferedOpsPerSecond"] == key[3], f"Wrong rate in an earlier window in {key}")
                checked_samples(earlier, key[3], seconds, workload, key)
                require(earlier["SchedulerLag"]["P99Ms"] > MAX_CACHE_SCHEDULER_P99_MS,
                        f"Scheduler retry replaced an eligible window in {key}")
            require(result["SchedulerLag"]["P99Ms"] <= MAX_CACHE_SCHEDULER_P99_MS or len(earlier_windows) == 2,
                    f"Missing scheduler retries in {key}")
        trials[key] = result
    require(not expected.difference(trials), f"Missing trials: {sorted(expected.difference(trials))}")
    if not cache:
        verify_health(measured["clusterHealth"])

    accepted = {}
    points = {}
    for workload in workloads:
        accepted[workload] = [
            rate for rate in rates
            if all(eligible(trials[system, repetition, workload, rate], scenario)
                   for system in versions for repetition in REPETITIONS)
        ]
        metric = "Write" if workload == "write" else "Read"
        points[workload] = [
            {"name": system, "version": version, "points": [
                {"offered": rate, "p99": median(trials[system, repetition, workload, rate][metric]["P99Ms"]
                                               for repetition in REPETITIONS)}
                for rate in accepted[workload]
            ]}
            for system, version in versions.items()
        ]
    return (accepted["mixed"] if cache else accepted), points
