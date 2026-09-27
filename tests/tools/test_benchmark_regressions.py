from __future__ import annotations

import contextlib
import csv
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools"))
import check_benchmark_regressions


class BenchmarkRegressionTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.baseline = self.root / "baseline.json"
        self.baseline.write_text(
            json.dumps(
                {
                    "maximumRegressionPercent": 30,
                    "timeBaselineCpuModel": "AMD EPYC 7763",
                    "timeBaselineJitTarget": "x86-64-v3",
                    "benchmarks": {
                        "ParseFixture": {
                            "meanNanoseconds": 100,
                            "allocatedBytes": 100,
                        }
                    },
                }
            ),
            encoding="utf-8",
        )
        self.reports = self.root / "reports"
        self.reports.mkdir()

    def write_samples(
        self,
        times: tuple[str, ...],
        allocations: tuple[str, ...],
        cpu_model: str = "AMD EPYC 7763",
        jit_target: str = "x86-64-v3",
    ) -> None:
        for index, (mean, allocated) in enumerate(zip(times, allocations, strict=True), start=1):
            results = self.reports / f"run-{index}" / "results"
            results.mkdir(parents=True)
            with (results / "benchmark-report.csv").open("w", newline="", encoding="utf-8") as stream:
                writer = csv.writer(stream)
                writer.writerow(("Method", "Mean", "Allocated"))
                writer.writerow(("ParseFixture", mean, allocated))
            report = results / "benchmark-report-github.md"
            report.write_text(
                "BenchmarkDotNet, Linux\n"
                f"{cpu_model} 2.45GHz, 1 CPU\n"
                f"  [Host] : .NET 10, X64 RyuJIT {jit_target}\n",
                encoding="utf-8",
            )

    def check(self) -> tuple[int, str]:
        output = io.StringIO()
        with contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
            result = check_benchmark_regressions.check(self.baseline, self.reports)
        return result, output.getvalue()

    def test_uses_median_time_to_ignore_one_runner_spike(self) -> None:
        self.write_samples(("140 ns", "100 ns", "100 ns"), ("100 B", "100 B", "100 B"))

        result, output = self.check()

        self.assertEqual(0, result, output)
        self.assertIn("median", output.lower())

    def test_fails_when_median_time_regresses(self) -> None:
        self.write_samples(("140 ns", "140 ns", "140 ns"), ("100 B", "100 B", "100 B"))

        result, output = self.check()

        self.assertEqual(1, result, output)
        self.assertIn("ParseFixture time", output)

    def test_checks_allocations_in_every_run_without_taking_a_median(self) -> None:
        self.write_samples(("100 ns", "100 ns", "100 ns"), ("100 B", "100 B", "131 B"))

        result, output = self.check()

        self.assertEqual(1, result, output)
        self.assertIn("ParseFixture allocations", output)

    def test_requires_three_reports_for_each_benchmark(self) -> None:
        self.write_samples(("100 ns", "100 ns"), ("100 B", "100 B"))

        with self.assertRaisesRegex(ValueError, "3 independent runs"):
            self.check()

    def test_skips_time_for_a_different_runner_but_checks_allocations(self) -> None:
        self.write_samples(
            ("140 ns", "140 ns", "140 ns"),
            ("100 B", "100 B", "100 B"),
            cpu_model="Intel Xeon Platinum 8370C",
            jit_target="x86-64-v4",
        )

        result, output = self.check()

        self.assertEqual(0, result, output)
        self.assertIn("time skipped", output)
        self.assertIn("allocations in run-3", output)

    def test_different_runner_does_not_skip_allocation_regressions(self) -> None:
        self.write_samples(
            ("140 ns", "140 ns", "140 ns"),
            ("100 B", "100 B", "131 B"),
            cpu_model="Intel Xeon Platinum 8370C",
            jit_target="x86-64-v4",
        )

        result, output = self.check()

        self.assertEqual(1, result, output)
        self.assertIn("time skipped", output)
        self.assertIn("allocations in run-3 regressed", output)


if __name__ == "__main__":
    unittest.main()
