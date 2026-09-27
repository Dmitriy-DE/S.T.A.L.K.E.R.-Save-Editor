# Companion installer archive lookup

The installer gets archive roots from the selected game's `fsgame.ltx`. It
resolves aliases whose names identify archive directories, follows parent
aliases such as `$fs_root$`, and honors the first alias flag: `true` searches
recursively and `false` searches only the configured directory. The second
flag controls X-Ray's user notification behavior and does not change archive
discovery. The installer does not infer archive locations from folder names.

When the same virtual file occurs more than once, the installer follows the
mount order used by X-Ray's locator: loose files in `gamedata/` take
precedence, later archive aliases in `fsgame.ltx` take precedence over earlier
aliases, and archive filenames within one alias are considered in ascending
relative-path order so a later patch such as `xpatch_02.db` overrides
`xpatch_01.db`. This follows the locator's ordered `fsgame.ltx` processing,
directory scan, and replacement of an already registered virtual path.

Reference implementation: [OpenXRay `LocatorAPI.cpp`](https://github.com/OpenXRay/xray-16/blob/dev/src/xrCore/LocatorAPI.cpp).
