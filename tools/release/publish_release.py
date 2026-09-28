"""Prepare and publish identical release bytes to GitHub and R2.

Port of the Python editor's tools/publish_release.py and build_release_manifest.py for the C#
packages. Stdlib only. The manifest (latest.json) has the schema StalkerSaveEditor.Updater reads:
schema, channel, version, source_commit, published_at, artifacts{target: {target, architecture,
kind, file, size, sha256, url}}, optional_artifacts.

    python tools/release/publish_release.py --artifacts dist --version 1.0.0 \
        --commit $(git rev-parse HEAD) --output release-output [--publish-r2 --verify-r2]

R2 upload uses Wrangler with CLOUDFLARE_API_TOKEN/CLOUDFLARE_ACCOUNT_ID from the environment;
latest.json is uploaded last so it is the channel's commit marker.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import subprocess
import time
from datetime import UTC, datetime
from pathlib import Path
from urllib.request import Request, urlopen

MANIFEST_SCHEMA = 1
DOWNLOAD_BASE_URL = "https://save-editor-downloads.save-editor.workers.dev"
BUCKET = "save-editor-downloads"

# target -> (kind, architecture, stable file name, glob of the build output)
TARGETS = {
    "windows-x86_64": ("portable", "x86_64", "SaveEditor-windows-x86_64.zip", "*windows*x64*.zip"),
    "windows-installer-x86_64": ("installer", "x86_64", "SaveEditor-windows-x86_64-setup.exe", "*[Ss]etup*.exe"),
    "linux-x86_64": ("portable", "x86_64", "SaveEditor-linux-x86_64.tar.gz", "*linux*x64*.tar.gz"),
    "linux-deb-amd64": ("package", "x86_64", "stalker-save-editor_amd64.deb", "stalker-save-editor_*_amd64.deb"),
    "macos-arm64": ("disk-image", "arm64", "SaveEditor-macos-arm64.dmg", "*arm64*.dmg"),
    "macos-x86_64": ("disk-image", "x86_64", "SaveEditor-macos-x86_64.dmg", "*osx-x64*.dmg"),
}
REQUIRED = ("windows-x86_64", "linux-x86_64", "linux-deb-amd64")
OPTIONAL = ("windows-installer-x86_64", "macos-arm64", "macos-x86_64")
CONTENT_TYPES = {
    ".zip": "application/zip",
    ".exe": "application/vnd.microsoft.portable-executable",
    ".gz": "application/gzip",
    ".deb": "application/vnd.debian.binary-package",
    ".dmg": "application/x-apple-diskimage",
    ".json": "application/json; charset=utf-8",
    ".asc": "application/pgp-keys",
    ".gpg": "application/octet-stream",
}
VERSION_RE = re.compile(r"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?$")


def sha256(path: Path) -> tuple[int, str]:
    digest = hashlib.sha256()
    size = 0
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            size += len(chunk)
            digest.update(chunk)
    return size, digest.hexdigest()


def reject_private_inputs(root: Path) -> None:
    rejected = [
        path
        for path in root.rglob("*")
        if path.is_file() and (path.suffix.casefold() in {".sav", ".scop", ".sacs", ".bak", ".pem", ".key"} or ".git" in path.parts)
    ]
    if rejected:
        names = ", ".join(str(path.relative_to(root)) for path in rejected[:5])
        raise ValueError(f"release input contains private files: {names}")


def locate(artifact_dir: Path, pattern: str, *, required: bool) -> Path | None:
    matches = sorted(path for path in artifact_dir.rglob(pattern) if path.is_file())
    if len(matches) > 1:
        raise ValueError(f"release artifact is ambiguous for {pattern}: {[m.name for m in matches]}")
    if not matches and required:
        raise ValueError(f"release artifact missing: {pattern}")
    return matches[0] if matches else None


def build_manifest(version: str, commit: str, published_at: str, artifacts: dict[str, Path]) -> dict[str, object]:
    if not VERSION_RE.match(version):
        raise ValueError(f"version must be semver: {version}")
    if len(commit) < 40 or any(ch not in "0123456789abcdef" for ch in commit):
        raise ValueError("commit must be a lowercase Git SHA")

    def describe(target: str) -> dict[str, object]:
        kind, architecture, filename, _ = TARGETS[target]
        size, digest = sha256(artifacts[target])
        return {
            "target": target,
            "architecture": architecture,
            "kind": kind,
            "file": filename,
            "size": size,
            "sha256": digest,
            "url": f"{DOWNLOAD_BASE_URL}/{filename}",
        }

    payload: dict[str, object] = {
        "schema": MANIFEST_SCHEMA,
        "channel": "stable",
        "version": version,
        "source_commit": commit,
        "published_at": published_at,
        "artifacts": {target: describe(target) for target in REQUIRED},
    }
    optional = {target: describe(target) for target in OPTIONAL if target in artifacts}
    if optional:
        payload["optional_artifacts"] = optional
    return payload


def prepare_release(artifact_dir: Path, output_dir: Path, version: str, commit: str, published_at: str) -> Path:
    """Copy build outputs to stable names, write latest.json and SHA256SUMS."""

    artifact_dir = artifact_dir.expanduser().resolve()
    output_dir = output_dir.expanduser().resolve()
    if not artifact_dir.is_dir():
        raise ValueError(f"artifact directory missing: {artifact_dir}")
    reject_private_inputs(artifact_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    stable: dict[str, Path] = {}
    for target, (_, _, filename, pattern) in TARGETS.items():
        source = locate(artifact_dir, pattern, required=target in REQUIRED)
        if source is None:
            continue
        destination = output_dir / filename
        shutil.copyfile(source, destination)
        stable[target] = destination
    manifest = build_manifest(version, commit, published_at, stable)
    (output_dir / "latest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    lines = [f"{sha256(path)[1]}  {path.name}" for target, path in stable.items()]
    (output_dir / "SHA256SUMS").write_text("\n".join(lines) + "\n", encoding="utf-8")
    return output_dir


def publication_files(output_dir: Path) -> list[tuple[str, Path]]:
    """Packages first, APT indexes after the pool with InRelease last, latest.json at the very end."""

    ordered: list[tuple[str, Path]] = []
    for _, _, filename, _ in TARGETS.values():
        path = output_dir / filename
        if path.is_file():
            ordered.append((filename, path))
    ordered.append(("SHA256SUMS", output_dir / "SHA256SUMS"))
    apt_root = output_dir / "apt"
    if apt_root.is_dir():
        def priority(path: Path) -> tuple[int, str]:
            relative = path.relative_to(output_dir).as_posix()
            if relative.startswith("apt/pool/"):
                return 0, relative
            if relative == "apt/repository-key.asc":
                return 1, relative
            if relative.endswith("/InRelease"):
                return 4, relative
            if relative.endswith("/Release") or relative.endswith("/Release.gpg"):
                return 3, relative
            return 2, relative

        for path in sorted((p for p in apt_root.rglob("*") if p.is_file()), key=priority):
            ordered.append((path.relative_to(output_dir).as_posix(), path))
    ordered.append(("latest.json", output_dir / "latest.json"))
    return ordered


def content_type(path: Path) -> str:
    if path.name in {"SHA256SUMS", "Packages", "Release", "InRelease"}:
        return "text/plain; charset=utf-8"
    if path.name.endswith(".tar.gz"):
        return "application/gzip"
    return CONTENT_TYPES.get(path.suffix.casefold(), "application/octet-stream")


def publish_r2(output_dir: Path, runner: str = "npx", wrangler_version: str = "4") -> None:
    for name, path in publication_files(output_dir):
        command = [
            runner, "--yes", f"wrangler@{wrangler_version}", "r2", "object", "put", f"{BUCKET}/{name}",
            "--file", str(path), "--remote", "--content-type", content_type(path),
        ]
        if name == "latest.json" or name.startswith("apt/dists/") or name == "apt/repository-key.asc":
            command += ["--cache-control", "public, max-age=60, must-revalidate"]
        else:
            command += ["--cache-control", "public, max-age=31536000, immutable",
                        "--content-disposition", f'attachment; filename="{path.name}"']
        subprocess.run(command, check=True)


def verify_r2(output_dir: Path, base_url: str = DOWNLOAD_BASE_URL, timeout: float = 30.0) -> None:
    """Read every public object back and compare bytes."""

    for name, local in publication_files(output_dir):
        request = Request(f"{base_url.rstrip('/')}/{name}?readback={int(time.time())}",
                          headers={"User-Agent": "SaveEditor-release-verifier/2"})
        with urlopen(request, timeout=timeout) as response:
            body = response.read()
        if body != local.read_bytes():
            raise ValueError(f"R2 read-back mismatch for {name}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--artifacts", type=Path)
    parser.add_argument("--version")
    parser.add_argument("--commit")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--prepared", action="store_true", help="publish an already prepared output directory")
    parser.add_argument("--published-at", default=datetime.now(UTC).isoformat().replace("+00:00", "Z"))
    parser.add_argument("--publish-r2", action="store_true")
    parser.add_argument("--verify-r2", action="store_true")
    args = parser.parse_args(argv)
    try:
        if args.prepared:
            output = args.output.resolve()
            if not (output / "latest.json").is_file():
                raise ValueError(f"prepared release has no latest.json: {output}")
        else:
            if args.artifacts is None or args.version is None or args.commit is None:
                parser.error("--artifacts, --version and --commit are required unless --prepared")
            output = prepare_release(args.artifacts, args.output, args.version, args.commit, args.published_at)
        if args.publish_r2:
            publish_r2(output)
        if args.verify_r2:
            verify_r2(output)
    except (OSError, ValueError, subprocess.CalledProcessError) as exc:
        parser.error(str(exc))
    print(output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
