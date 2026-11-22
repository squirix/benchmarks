import importlib.util
import unittest
from copy import deepcopy
from pathlib import Path

from benchmark_results import REPETITIONS, summarize, verify_health


def load_script(name):
    path = Path(__file__).with_name(name)
    spec = importlib.util.spec_from_file_location(path.stem, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


validate = load_script("validate-results.py")


def distribution(count, p99):
    return {"Count": count, "P50Ms": 0, "P95Ms": 0, "P99Ms": p99}


def cache_measurements():
    versions = {"Squirix": "0.1.0-preview.10", "Garnet": "2.2.13", "Valkey": "9.2.0", "Redis": "8.10.3"}
    trials = []
    for system in versions:
        for rate in (200, 400):
            for repetition in REPETITIONS:
                trials.append({
                    "System": system, "Repetition": repetition, "EarlierWindows": [], "Result": {
                        "OfferedOpsPerSecond": rate, "Offered": rate, "Seconds": 1, "Dropped": 0,
                        "CompletedWithinWindow": rate, "Read": distribution(rate * 19 // 20, repetition),
                        "Write": distribution(rate // 20, 0.1), "ReadService": distribution(rate * 19 // 20, 0.1),
                        "SchedulerLag": distribution(rate, 0.1),
                    },
                })
    return {"systems": versions, "testedRates": [200, 400], "secondsPerPoint": 1, "trials": trials}


def quorum_health():
    snapshots = []
    for repetition in REPETITIONS:
        for phase in ("before", "after"):
            snapshots.append({"System": "Squirix", "Repetition": repetition, "Phase": phase, "Nodes": [
                {"NodeId": "A", "Copies": [{"NodeId": peer, "State": "ready", "LagEntries": 0} for peer in "BC"]},
                {"NodeId": "B", "Copies": []},
                {"NodeId": "C", "Copies": []},
            ]})
            snapshots.append({"System": "etcd", "Repetition": repetition, "Phase": phase, "Nodes": [
                {"Id": node, "Endpoint": f"https://node-{node}:2379", "Leader": "a", "Term": 2} for node in "abc"
            ]})
    return snapshots


class TrialValidation(unittest.TestCase):
    def test_scheduler_budget_excludes_rate_for_all_systems(self):
        measured = cache_measurements()
        slow = measured["trials"][3]
        slow["Result"]["SchedulerLag"]["P99Ms"] = 0.501
        slow["EarlierWindows"] = [deepcopy(slow["Result"]) for _ in range(2)]
        measured["trials"][2]["Result"]["SchedulerLag"]["P99Ms"] = 0.5
        accepted, charts = summarize("cache", measured)
        self.assertEqual([200], accepted)
        self.assertEqual([[{"offered": 200, "p99": 2}]] * 4, [system["points"] for system in charts["mixed"]])

    def test_requires_every_trial_even_for_a_rejected_rate(self):
        measured = cache_measurements()
        slow = measured["trials"][3]
        slow["Result"]["SchedulerLag"]["P99Ms"] = 3
        slow["EarlierWindows"] = [deepcopy(slow["Result"]) for _ in range(2)]
        for positions in ((3,), (3, 4, 5), (1,)):
            with self.subTest(positions=positions):
                incomplete = {**measured, "trials": [row for index, row in enumerate(measured["trials"]) if index not in positions]}
                with self.assertRaisesRegex(ValueError, "Missing trials"):
                    summarize("cache", incomplete)

    def test_rejects_retry_after_scheduler_qualified_window(self):
        measured = cache_measurements()
        measured["trials"][3]["EarlierWindows"].append(deepcopy(measured["trials"][3]["Result"]))
        with self.assertRaisesRegex(ValueError, "replaced an eligible window"):
            summarize("cache", measured)

    def test_rejects_duplicate_repetition(self):
        measured = cache_measurements()
        measured["trials"][1] = measured["trials"][0]
        with self.assertRaisesRegex(ValueError, "duplicate trial"):
            summarize("cache", measured)

    def test_rejects_impossible_completion_count(self):
        measured = cache_measurements()
        measured["trials"][0]["Result"]["CompletedWithinWindow"] = 201
        with self.assertRaisesRegex(ValueError, "Inconsistent operation counters"):
            summarize("cache", measured)

    def test_rejects_incorrect_published_version(self):
        measured = cache_measurements()
        accepted, charts = summarize("cache", measured)
        measured["acceptedChartRates"] = accepted
        run = {"report": "runs/test/", "version": "v0.1.0-preview.10", "referenceLoad": 200, "systems": charts["mixed"]}
        validate.verify_run("cache", run, measured)
        run["systems"][0]["version"] = "99.0.0"
        with self.assertRaisesRegex(ValueError, "Wrong version for Squirix"):
            validate.verify_run("cache", run, measured)

    def test_rejects_manually_omitted_qualified_rate(self):
        measured = cache_measurements()
        _, charts = summarize("cache", measured)
        measured["acceptedChartRates"] = [200]
        run = {"report": "runs/test/", "version": "v0.1.0-preview.10", "referenceLoad": 200, "systems": charts["mixed"]}
        with self.assertRaisesRegex(ValueError, "Chart rates fail the run policy"):
            validate.verify_run("cache", run, measured)


class ClusterValidation(unittest.TestCase):
    def test_requires_all_cluster_snapshots(self):
        valid = quorum_health()
        verify_health(valid)
        with self.assertRaisesRegex(ValueError, "Missing before/after"):
            verify_health(valid[:-1])

    def test_rejects_recovering_squirix_copy(self):
        evidence = quorum_health()
        evidence[2]["Nodes"][0]["Copies"][1]["State"] = "recovering"
        with self.assertRaisesRegex(ValueError, "copies must both be ready"):
            verify_health(evidence)

    def test_rejects_new_etcd_raft_term(self):
        evidence = quorum_health()
        for node in evidence[3]["Nodes"]:
            node["Term"] = 3
        with self.assertRaisesRegex(ValueError, "leadership changed"):
            verify_health(evidence)
