#!/usr/bin/env python3
"""Generate cloud transaction fixtures from the read-only Python oracle."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import runpy
import subprocess
import sys
import tempfile


REMOTE_PATH = "Stalker2/Saved/STEAM/SaveGames/Data/slot.sav"
MONEY = 900_000
PERSISTED_TIMEOUT = 7


class FakeCloud:
    def __init__(self, data: bytes, capability_type: type, *, mode: str = "verified") -> None:
        self.files = {REMOTE_PATH: data}
        self.mode = mode
        self.read_calls: list[str] = []
        self.write_calls: list[tuple[str, bytes]] = []
        self.sync_calls = 0
        self.wait_calls: list[tuple[str, int, int]] = []
        self.write_capability = capability_type(True, "fixture writer ready")

    def read_file(self, filename: str) -> bytes:
        self.read_calls.append(filename)
        return self.files[filename]

    def write_file(self, filename: str, data: bytes) -> None:
        self.write_calls.append((filename, data))
        self.files[filename] = data
        if self.mode == "write_error":
            raise RuntimeError("fixture write failed after send")

    def sync(self) -> None:
        self.sync_calls += 1

    def wait_persisted(self, filename: str, expected_size: int, timeout: int = 120) -> bool:
        self.wait_calls.append((filename, expected_size, timeout))
        return True


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def generate(python_repo: Path, fixture_dir: Path, golden_path: Path) -> None:
    python_repo = python_repo.expanduser().resolve()
    sys.path.insert(0, str(python_repo))

    from editor.cloud_capabilities import CloudWriteCapability
    from editor.codec import load_encoder
    from editor.models import EditPlan, SourceRef
    from editor.prepare import prepare_edit
    from editor.transactions import CloudTransactionError, upload_cloud
    import save_format as sf

    try:
        load_encoder()
    except Exception as error:
        raise RuntimeError(
            "A Python Kraken encoder is required for byte-parity cloud fixtures; "
            "build it with tools/build_ooz_encoder.py and add its output to PYTHONPATH"
        ) from error

    fixture = runpy.run_path(str(python_repo / "tests" / "conftest.py"))["synthetic_save"]
    source = fixture.__wrapped__()
    source_sha = sha256(source)
    prepared = prepare_edit(
        source,
        EditPlan(
            source=SourceRef(kind="cloud", locator=REMOTE_PATH, sha256=source_sha),
            money=MONEY,
        ),
    )
    edited = bytes(prepared.data)
    edited_sha = sha256(edited)
    edited_raw = sf.decompress_save(edited)
    edited_raw_sha = sha256(edited_raw)

    with tempfile.TemporaryDirectory(prefix="cloud-transaction-oracle-") as backup_directory:
        verified_worker = FakeCloud(source, CloudWriteCapability)
        receipt = upload_cloud(
            verified_worker,
            prepared,
            Path(backup_directory),
            persisted_timeout=PERSISTED_TIMEOUT,
        )
        if receipt.status != "verified" or receipt.backup_path.read_bytes() != source:
            raise RuntimeError("Python oracle did not verify the cloud transaction fixture")
        if receipt.recovery_path.read_bytes() != edited:
            raise RuntimeError("Python oracle recovery fixture differs from prepared bytes")
        verified = {
            "status": receipt.status,
            "reads": verified_worker.read_calls,
            "writes": len(verified_worker.write_calls),
            "syncs": verified_worker.sync_calls,
            "waits": [list(call) for call in verified_worker.wait_calls],
            "backup_sha256": sha256(receipt.backup_path.read_bytes()),
            "recovery_sha256": sha256(receipt.recovery_path.read_bytes()),
        }

        stale_worker = FakeCloud(b"changed source", CloudWriteCapability)
        try:
            upload_cloud(stale_worker, prepared, Path(backup_directory) / "stale")
        except CloudTransactionError as error:
            stale = {
                "error_contains": "изменился" in str(error),
                "writes": len(stale_worker.write_calls),
                "artifacts": list((Path(backup_directory) / "stale").glob("*")),
            }
        else:
            raise RuntimeError("Python oracle accepted a stale cloud source")
        if not stale["error_contains"] or stale["writes"] != 0 or stale["artifacts"]:
            raise RuntimeError("Python oracle stale-source contract changed")
        stale["artifacts"] = len(stale["artifacts"])

        uncertain_worker = FakeCloud(source, CloudWriteCapability, mode="write_error")
        uncertain_receipt = upload_cloud(
            uncertain_worker,
            prepared,
            Path(backup_directory) / "uncertain",
        )
        if uncertain_receipt.status != "uncertain" or len(uncertain_worker.write_calls) != 1:
            raise RuntimeError("Python oracle did not return one uncertain write attempt")
        uncertain = {
            "status": uncertain_receipt.status,
            "reason_contains": "WriteFile" in (uncertain_receipt.reason or ""),
            "writes": len(uncertain_worker.write_calls),
            "syncs": uncertain_worker.sync_calls,
            "recovery_sha256": sha256(uncertain_receipt.recovery_path.read_bytes()),
        }

    manifest = {
        "oracle_revision": subprocess.run(
            ["git", "-C", str(python_repo), "rev-parse", "HEAD"],
            check=True,
            capture_output=True,
            text=True,
        ).stdout.strip(),
        "remote_path": REMOTE_PATH,
        "source_file": "cloud-slot-source.sav",
        "source_sha256": source_sha,
        "expected_file": "cloud-slot-expected.sav",
        "expected_sha256": edited_sha,
        "expected_raw_file": "cloud-slot-expected.raw",
        "expected_raw_sha256": edited_raw_sha,
        "money": MONEY,
        "persisted_timeout": PERSISTED_TIMEOUT,
        "cases": {"verified": verified, "stale_source": stale, "uncertain_write": uncertain},
    }
    fixture_dir.mkdir(parents=True, exist_ok=True)
    golden_path.parent.mkdir(parents=True, exist_ok=True)
    (fixture_dir / manifest["source_file"]).write_bytes(source)
    (fixture_dir / manifest["expected_file"]).write_bytes(edited)
    (fixture_dir / manifest["expected_raw_file"]).write_bytes(edited_raw)
    golden_path.write_text(
        json.dumps(manifest, ensure_ascii=False, sort_keys=True, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    print(f"Generated cloud transaction fixtures from {manifest['oracle_revision']}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--python-repo", type=Path, required=True)
    parser.add_argument(
        "--fixture-dir",
        type=Path,
        default=Path(__file__).parents[1] / "tests" / "Fixtures" / "cloud-transaction",
    )
    parser.add_argument(
        "--golden-path",
        type=Path,
        default=Path(__file__).parents[1] / "tests" / "golden" / "cloud-transaction" / "cloud-transaction.json",
    )
    args = parser.parse_args()
    generate(args.python_repo, args.fixture_dir, args.golden_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
