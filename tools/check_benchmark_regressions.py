#!/usr/bin/env python3
"""Fail when short-run BenchmarkDotNet time or allocation regresses by >30%."""

from __future__ import annotations

import argparse
import csv
import json
import re
import statistics
import sys
from pathlib import Path


EXPECTED_RUNS = 3
MAXIMUM_ABSOLUTE_TIME_RATIO = 2.0
TIME_UNITS_TO_NS = {"ns": 1.0, "us": 1_000.0, "µs": 1_000.0, "ms": 1_000_000.0, "s": 1_000_000_000.0}
BYTE_UNITS_TO_BYTES = {"b": 1.0, "kb": 1024.0, "kib": 1024.0, "mb": 1024.0**2, "mib": 1024.0**2}
MEASUREMENT = re.compile(r"^\s*([\d,]+(?:\.\d+)?)\s*([a-zA-Zµ]+)\s*$")
JIT_TARGET = re.compile(r"^\s*\[Host\].*RyuJIT\s+([A-Za-z0-9_-]+)\s*$", re.MULTILINE)
CPU_CLOCK = re.compile(r"\s+\d+(?:\.\d+)?GHz.*$")


def parse_measurement(value: str, scales: dict[str, float], label: str) -> float:
    match = MEASUREMENT.match(value)
    if match is None:
        raise ValueError(f"Cannot parse {label} measurement: {value!r}")
    amount = float(match.group(1).replace(",", ""))
    unit = match.group(2).lower()
    try:
        return amount * scales[unit]
    except KeyError as error:
        raise ValueError(f"Unsupported {label} unit: {unit!r}") from error


def read_runner_fingerprint(report_path: Path) -> tuple[str, str]:
    report = report_path.with_name(report_path.name.replace("-report.csv", "-report-github.md"))
    if not report.is_file():
        raise ValueError(f"BenchmarkDotNet runner metadata is missing: {report.name}")

    lines = report.read_text(encoding="utf-8").splitlines()
    cpu_line = next((line.strip() for line in lines if "GHz" in line and "," in line), None)
    jit_match = JIT_TARGET.search("\n".join(lines))
    if cpu_line is None or jit_match is None:
        raise ValueError(f"BenchmarkDotNet runner metadata is incomplete: {report.name}")

    cpu_model = CPU_CLOCK.sub("", cpu_line.split(",", maxsplit=1)[0]).removesuffix(" CPU").strip()
    return cpu_model, jit_match.group(1)


def read_measurements(report_dir: Path) -> dict[str, dict[str, dict[str, float | str]]]:
    found: dict[str, dict[str, dict[str, float | str]]] = {}
    report_paths = sorted(report_dir.rglob("*-report.csv"))
    if not report_paths:
        raise ValueError("No BenchmarkDotNet report CSV files were found.")

    for csv_path in report_paths:
        relative_path = csv_path.relative_to(report_dir)
        if len(relative_path.parts) < 2:
            raise ValueError("Each benchmark report must be inside its independent run directory.")
        run_id = relative_path.parts[0]
        cpu_model, jit_target = read_runner_fingerprint(csv_path)
        with csv_path.open(newline="", encoding="utf-8-sig") as stream:
            for row in csv.DictReader(stream):
                method = row.get("Method", "").strip()
                if not method:
                    continue
                runs = found.setdefault(method, {})
                if run_id in runs:
                    raise ValueError(f"Duplicate BenchmarkDotNet result for {method} in run {run_id}.")
                runs[run_id] = {
                    "meanNanoseconds": parse_measurement(row["Mean"], TIME_UNITS_TO_NS, "time"),
                    "allocatedBytes": parse_measurement(row["Allocated"], BYTE_UNITS_TO_BYTES, "allocation"),
                    "cpuModel": cpu_model,
                    "jitTarget": jit_target,
                }
    return found


def check(baseline_path: Path, report_dir: Path) -> int:
    baseline = json.loads(baseline_path.read_text(encoding="utf-8"))
    expected = baseline.get("benchmarks")
    if not isinstance(expected, dict) or not expected:
        raise ValueError("Baseline must contain a non-empty benchmarks object.")
    threshold = 1 + float(baseline["maximumRegressionPercent"]) / 100
    baseline_cpu = baseline.get("timeBaselineCpuModel")
    baseline_jit = baseline.get("timeBaselineJitTarget")
    if not isinstance(baseline_cpu, str) or not isinstance(baseline_jit, str):
        raise ValueError("Baseline must specify timeBaselineCpuModel and timeBaselineJitTarget.")
    observed = read_measurements(report_dir)
    missing = sorted(set(expected) - set(observed))
    if missing:
        raise ValueError(f"Benchmark reports are missing methods: {', '.join(missing)}")

    failures: list[str] = []
    skipped_time_methods: list[str] = []
    time_ratios: dict[str, float] = {}
    for method, reference in expected.items():
        runs = observed[method]
        if len(runs) != EXPECTED_RUNS:
            raise ValueError(
                f"{method} has {len(runs)} independent reports; expected exactly {EXPECTED_RUNS} independent runs."
            )

        matching_runs = [
            result for result in runs.values()
            if result["cpuModel"] == baseline_cpu and result["jitTarget"] == baseline_jit
        ]
        if len(matching_runs) == EXPECTED_RUNS:
            time_baseline = float(reference["meanNanoseconds"])
            median_time = statistics.median(result["meanNanoseconds"] for result in matching_runs)
            time_ratio = median_time / time_baseline if time_baseline > 0 else (
                1.0 if median_time == 0 else float("inf")
            )
            print(
                f"{method} time median of {EXPECTED_RUNS}: {median_time:.2f} vs baseline "
                f"{time_baseline:.2f} ({time_ratio:.2f}x)"
            )
            time_ratios[method] = time_ratio
        else:
            fingerprints = sorted({f"{result['cpuModel']} / {result['jitTarget']}" for result in runs.values()})
            skipped_time_methods.append(method)
            print(
                f"{method} time skipped: runner {', '.join(fingerprints)} does not match "
                f"baseline {baseline_cpu} / {baseline_jit}"
            )

        allocation_baseline = float(reference["allocatedBytes"])
        for run_id, result in sorted(runs.items()):
            actual_value = result["allocatedBytes"]
            ratio = actual_value / allocation_baseline if allocation_baseline > 0 else (
                1.0 if actual_value == 0 else float("inf")
            )
            label = f"{method} allocations in {run_id}"
            print(f"{label}: {actual_value:.2f} vs baseline {allocation_baseline:.2f} ({ratio:.2f}x)")
            if ratio > threshold:
                failures.append(f"{label} regressed {ratio:.2f}x (limit {threshold:.2f}x)")

    # A slower hosted runner makes every benchmark slower by about the same factor. With three or more
    # comparable methods the median slowdown is treated as runner speed; a method still fails when it is
    # slower than the others by more than the threshold, or slower than the baseline by more than 2x.
    runner_factor = statistics.median(time_ratios.values()) if len(time_ratios) >= 3 else 1.0
    runner_factor = max(runner_factor, 1.0)
    if runner_factor > 1.0:
        print(f"Runner speed factor (median time ratio): {runner_factor:.2f}x")
    absolute_limit = max(threshold, MAXIMUM_ABSOLUTE_TIME_RATIO)
    for method, time_ratio in time_ratios.items():
        relative = time_ratio / runner_factor
        if relative > threshold:
            failures.append(
                f"{method} median time regressed {time_ratio:.2f}x "
                f"({relative:.2f}x after runner factor, limit {threshold:.2f}x)"
            )
        elif time_ratio > absolute_limit:
            failures.append(f"{method} median time regressed {time_ratio:.2f}x (absolute limit {absolute_limit:.2f}x)")

    if failures:
        print("Performance regression threshold exceeded:", file=sys.stderr)
        for failure in failures:
            print(f"- {failure}", file=sys.stderr)
        return 1
    print(
        f"Comparable median times across {EXPECTED_RUNS} runs and per-run allocations are within "
        f"the {baseline['maximumRegressionPercent']}% threshold; time was not compared for "
        f"{len(skipped_time_methods)} methods on a different runner fingerprint."
    )
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--reports", type=Path, required=True)
    args = parser.parse_args()
    return check(args.baseline, args.reports)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, KeyError, json.JSONDecodeError) as error:
        print(f"Benchmark regression check failed: {error}", file=sys.stderr)
        raise SystemExit(2)
