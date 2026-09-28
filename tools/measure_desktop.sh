#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp_base="${TMPDIR:-${HOME:?HOME must be set}/.cache/codex-tmp}"
case "$tmp_base" in
  /tmp|/tmp/*)
    echo "Refusing to use /tmp; set TMPDIR to a directory under ~/.cache/codex-tmp." >&2
    exit 2
    ;;
esac
mkdir -p "$tmp_base"
task_root="$(mktemp -d "$tmp_base/measure-desktop.XXXXXX")"
created_native_library=0

cleanup() {
  if (( created_native_library )); then
    rm -f "$repo_root/artifacts/native/libstalker_ooz.so"
    rmdir "$repo_root/artifacts/native" "$repo_root/artifacts" 2>/dev/null || true
  fi
  rm -rf "$task_root"
}
trap cleanup EXIT

export HOME="$task_root/home"
export TMPDIR="$task_root/tmp"
export DOTNET_CLI_HOME="$HOME"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$task_root/nuget}"
mkdir -p "$HOME" "$TMPDIR"

native_library="$repo_root/artifacts/native/libstalker_ooz.so"
if [[ ! -f "$native_library" ]]; then
  if [[ -z "${PYTHON_REPO:-}" ]]; then
    echo "PYTHON_REPO must point to the read-only pinned Python oracle when the Kraken library is absent." >&2
    exit 2
  fi
  python3 "$repo_root/tools/build_ooz_native.py" --python-repo "$PYTHON_REPO" --output-dir "$repo_root/artifacts/native"
  created_native_library=1
fi

fixtures=(
  "$repo_root/tests/Fixtures/writer-money/xray-money-soc-source.sav"
  "$repo_root/tests/Fixtures/writer-money/xray-money-soc-ee-source.sav"
  "$repo_root/tests/Fixtures/writer-money/xray-money-cs-source.sav"
  "$repo_root/tests/Fixtures/writer-money/xray-money-cs-ee-source.sav"
  "$repo_root/tests/Fixtures/writer-money/xray-money-cop-source.sav"
  "$repo_root/tests/Fixtures/writer-money/xray-money-cop-ee-source.sav"
  "$repo_root/tests/Fixtures/synthetic-s2.sav"
)
for fixture in "${fixtures[@]}"; do
  if [[ ! -f "$fixture" ]]; then
    echo "Missing synthetic save fixture: ${fixture##*/}" >&2
    exit 2
  fi
done

artifacts="$task_root/artifacts"
dotnet build "$repo_root/tools/measure_desktop/MeasureDesktop.csproj" \
  --configuration Release --artifacts-path "$artifacts" -warnaserror
harness="$(find "$artifacts/bin/MeasureDesktop" -type f -name MeasureDesktop.dll -print -quit)"
if [[ -z "$harness" ]]; then
  echo "Could not locate the built headless measurement harness." >&2
  exit 1
fi

cat > "$task_root/measure_startup.py" <<'PY'
import statistics
import subprocess
import sys
import time

dotnet, assembly, temporary_root = sys.argv[1:]
samples = []
for index in range(5):
    started = time.perf_counter_ns()
    completed = subprocess.run(
        [dotnet, assembly, temporary_root, "--launch-only"],
        check=False,
        capture_output=True,
        text=True,
    )
    if completed.returncode != 0:
        sys.stderr.write(completed.stderr)
        sys.stdout.write(completed.stdout)
        raise SystemExit(completed.returncode)
    elapsed_ms = (time.perf_counter_ns() - started) / 1_000_000
    startup_ms = next(
        float(line.split("=", 1)[1])
        for line in completed.stdout.splitlines()
        if line.startswith("desktop_startup_ms=")
    )
    samples.append(elapsed_ms)
    print(f"launch_{index + 1}_process_ms={elapsed_ms:.2f} app_setup_ms={startup_ms:.2f}")
print(f"process_start_median_ms={statistics.median(samples):.2f}")
print(f"process_start_min_ms={min(samples):.2f} process_start_max_ms={max(samples):.2f}")
PY

echo "Headless Desktop launch samples (five fresh processes):"
python3 "$task_root/measure_startup.py" "$(command -v dotnet)" "$harness" "$task_root"

echo "Headless Desktop scenario (20 sequential opens across seven synthetic formats):"
/usr/bin/time -f 'scenario_elapsed_s=%e scenario_max_rss_kb=%M' \
  dotnet "$harness" "$task_root" "${fixtures[@]}"
