# CS-5: X-Ray item removal

`XRayDeleteWriter` removes an item registry record for the six original and
Enhanced Edition releases recognized by the X-Ray readers. It matches the
Python `prepare_xray` detach behavior and the maturity values in the Python
capability registry: the original releases are `verified`; Enhanced Edition
releases are `experimental`.

The writer removes only a leaf item whose parent is the actor. It rejects the
actor record, objects owned by another registry entry, items with registry
children, known equipped items, missing handles, unresolved ammo, stale source
hashes, and plans that combine deletion with money or stack edits. All other
OBJECT records and chunks are retained. The output is reparsed and checked to
confirm the release is unchanged and the requested handles are absent.

The dependency check uses the parsed `parent_id` registry edges. It does not
interpret opaque object state as a reference graph, matching the current
Python oracle's deletion preflight. Synthetic vectors are generated with
`tools/generate_xray_delete_fixtures.py` from the Python repository and compare
both the rebuilt container and unpacked bytes for each release. This PR does
not add live game load/save evidence.
