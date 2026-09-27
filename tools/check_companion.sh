#!/usr/bin/env bash
# Syntax-check the companion scripts with Lua 5.1 (the X-Ray script dialect).
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
luac="$(command -v luac5.1 || command -v luac)"
status=0
while IFS= read -r -d '' file; do
	"$luac" -p "$file" || status=1
done < <(find "$root/mods/companion" -name '*.script' -print0)
python3 "$root/tools/check_companion_ui.py" || status=1
exit $status
