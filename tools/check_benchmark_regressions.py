#!/usr/bin/env python3
"""Fail when short-run BenchmarkDotNet time or allocation regresses by >30%."""

from __future__ import annotations

import argparse
import csv
import json
import re
import sys
from pathlib import Path


TIME_UNITS_TO_NS = {"ns": 1.0, "us": 1_000.0, "µs": 1_000.0, "ms": 1_000_000.0, "s": 1_000_000_000.0}
BYTE_UNITS_TO_BYTES = {"b": 1.0, "kb": 1024.0, "kib": 1024.0, "mb": 1024.0**2, "mib": 1024.0**2}
MEASUREMENT = re.compile(r"^\s*([\d,]+(?:\.\d+)?)\s*([a-zA-Zµ]+)\s*$")


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


def read_measurements(report_dir: Path) -> dict[str, dict[str, float]]:
    found: dict[str, dict[str, float]] = {}
    for csv_path in sorted(report_dir.glob("*-report.csv")):
        with csv_path.open(newline="", encoding="utf-8-sig") as stream:
            for row in csv.DictReader(stream):
                method = row.get("Method", "").strip()
                if not method:
                    continue
                if method in found:
                    raise ValueError(f"Duplicate BenchmarkDotNet result for {method}.")
                found[method] = {
                    "meanNanoseconds": parse_measurement(row["Mean"], TIME_UNITS_TO_NS, "time"),
                    "allocatedBytes": parse_measurement(row["Allocated"], BYTE_UNITS_TO_BYTES, "allocation"),
                }
    return found


def check(baseline_path: Path, report_dir: Path) -> int:
    baseline = json.loads(baseline_path.read_text(encoding="utf-8"))
    expected = baseline.get("benchmarks")
    if not isinstance(expected, dict) or not expected:
        raise ValueError("Baseline must contain a non-empty benchmarks object.")
    threshold = 1 + float(baseline["maximumRegressionPercent"]) / 100
    observed = read_measurements(report_dir)
    missing = sorted(set(expected) - set(observed))
    if missing:
        raise ValueError(f"Benchmark reports are missing methods: {', '.join(missing)}")

    failures: list[str] = []
    for method, reference in expected.items():
        result = observed[method]
        for field, label in (("meanNanoseconds", "time"), ("allocatedBytes", "allocations")):
            baseline_value = float(reference[field])
            actual_value = result[field]
            ratio = actual_value / baseline_value if baseline_value > 0 else (1.0 if actual_value == 0 else float("inf"))
            print(f"{method} {label}: {actual_value:.2f} vs baseline {baseline_value:.2f} ({ratio:.2f}x)")
            if ratio > threshold:
                failures.append(f"{method} {label} regressed {ratio:.2f}x (limit {threshold:.2f}x)")

    if failures:
        print("Performance regression threshold exceeded:", file=sys.stderr)
        for failure in failures:
            print(f"- {failure}", file=sys.stderr)
        return 1
    print(f"All benchmarks are within the {baseline['maximumRegressionPercent']}% time/allocation threshold.")
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
