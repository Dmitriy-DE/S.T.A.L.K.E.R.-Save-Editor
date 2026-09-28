# Third-party notices

The editor is licensed under the GNU GPL v3 (see `LICENSE`), required by the Kraken decoder below.

## ooz / pyooz (Kraken)

S.T.A.L.K.E.R. 2 saves are Kraken-compressed. `libstalker_ooz` (desktop) and its WebAssembly build are
compiled from the pyooz 0.0.8 sources vendored in `third_party/pyooz` (hash in `provenance.json`).
License: GNU GPL v3 or later.

## Valve `libsteam_api`

Steam Cloud and achievements call Valve's `libsteam_api` (Steamworks SDK, proprietary to Valve) in a
separate worker process. It is **not bundled**: the copy from the Steam runtime or an installed Steam
game is loaded. Steam's subscriber agreement and the Steamworks SDK license govern its use.

## Libraries

| Library | License |
|---|---|
| Avalonia (UI, desktop and browser) | MIT |
| NVorbis (OGG decoding of the games' menu sounds) | MIT |
| .NET runtime and libraries | MIT |

## Game media

Menu sounds, music, fonts and item icons of the S.T.A.L.K.E.R. games belong to GSC Game World and are used
to show the games' own content. The source of every file is recorded in
`src/StalkerSaveEditor.Desktop/Assets/PROVENANCE.json`. Wiki-derived names follow the source wiki's
license (Fandom text: CC BY-SA 3.0); records marked `fextralife` identify the source only and grant no
reuse license by themselves.
