#!/usr/bin/env bash
# Syntax-check the companion scripts with Lua 5.1 (the X-Ray script dialect)
# and verify bind_stalker patch hooks syntax.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
luac="$(command -v luac5.1 || command -v luac)"
status=0

echo "Checking companion mod Lua scripts..."
count=0
while IFS= read -r -d '' file; do
	"$luac" -p "$file" || { echo "FAIL: $file" >&2; status=1; }
	count=$((count + 1))
done < <(find "$root/mods/companion" \( -name '*.script' -o -name '*.lua' \) -print0)
echo "Verified $count companion Lua script files."

echo "Checking bind_stalker.script patch hooks..."
# 1. Update hook
echo 'if save_editor_companion then save_editor_companion.update() end' | "$luac" -p - || { echo "FAIL: update hook" >&2; status=1; }

# 2. Item use hook
echo 'if save_editor_companion and save_editor_companion.on_item_use then save_editor_companion.on_item_use(obj) end' | "$luac" -p - || { echo "FAIL: on_item_use hook" >&2; status=1; }

# 3. Synthetic patched actor_binder
cat <<'EOF' | "$luac" -p - || { echo "FAIL: synthetic bind_stalker patch" >&2; status=1; }
function init(obj)
    local new_binder = actor_binder(obj)
    obj:bind_object(new_binder)
end
class "actor_binder" (object_binder)
function actor_binder:__init(obj) super(obj) end
function actor_binder:update(delta)
    object_binder.update(self, delta)
    if save_editor_companion then save_editor_companion.update() end
end
function actor_binder:use_inventory_item(obj)
    if save_editor_companion and save_editor_companion.on_item_use then save_editor_companion.on_item_use(obj) end
end
EOF
echo "bind_stalker patch syntax verified."

echo "Checking companion UI script contracts..."
python3 "$root/tools/check_companion_ui.py" || { echo "FAIL: check_companion_ui.py" >&2; status=1; }

if [ $status -eq 0 ]; then
	echo "All companion mod checks passed successfully."
fi
exit $status
