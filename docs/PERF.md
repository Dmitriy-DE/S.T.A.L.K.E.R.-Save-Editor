# Performance and memory observations

Measurements are observations from the stated local environment, not CI thresholds. Synthetic fixtures are used; these results do not describe the memory footprint of the packaged desktop UI or prove in-game behavior.

## Desktop headless observation (E1)

Command: `tools/measure_desktop.sh`. Environment: Linux x64, .NET SDK 10.0.112. It used a temporary `HOME` and `TMPDIR`, synthetic save fixtures for seven formats, five fresh headless process starts, and 20 sequential open/close cycles through the Desktop `SaveLibraryViewModel`.

| Measurement | Result |
| --- | ---: |
| Process start + headless window, median (5 runs) | 1,514 ms |
| Process start minimum / maximum | 1,449 ms / 1,831 ms |
| Idle managed heap after full GC | 9,140,656 B |
| Managed heap after first close | 16,042,952 B |
| Managed heap after 20th close | 16,106,288 B |
| Growth from first close to 20th close | 63,336 B |
| Managed heap after Desktop close and full GC | 16,085,488 B |
| Idle working set | 109,178,880 B |
| Scenario process maximum RSS | 159,380 KiB (~155.6 MiB) |

The harness rendered an empty startup window and one final synthetic save. The 20-cycle memory values are from one scenario run. During the open/close cycles, the changing ViewModel collection was not bound to a window: binding and clearing it reproduces a Desktop `NullReferenceException` in item templates. Issue [#90](https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor-Next/issues/90) tracks the UI fix. The heap result therefore covers the Desktop ViewModel/Core lifecycle, not interactive save switching or UI collection recycling. The post-close heap remains about 6.9 MB above the idle baseline after the first parse/render warm-up; this single run does not identify that retained memory as a leak.

## Core open-edit-close cycles (E2)

Command:

```sh
dotnet test tests/StalkerSaveEditor.Core.Tests/StalkerSaveEditor.Core.Tests.csproj -c Release \
  --filter FullyQualifiedName~One_hundred_open_edit_close_cycles_release_save_buffers \
  --logger 'console;verbosity=detailed'
```

Each release used 10 warm-up cycles followed by 100 cycles that opened a synthetic save, parsed it, prepared a money edit, verified the read-back, and released the stream and buffers. Managed heap was read after full GC before and after the measured cycles. The test also checked weak references to all 100 source buffers; zero remained alive for every format.

Environment: Linux x64, .NET 10.0.12, Release, one local run.

| Format | Managed heap before | After | Delta | Source buffers retained |
| --- | ---: | ---: | ---: | ---: |
| S.T.A.L.K.E.R. 2 | 3,798,336 B | 3,798,112 B | -224 B | 0 / 100 |
| SoC EE | 3,809,240 B | 3,809,240 B | 0 B | 0 / 100 |
| Clear Sky | 3,811,488 B | 3,811,488 B | 0 B | 0 / 100 |
| Clear Sky EE | 3,813,720 B | 3,813,720 B | 0 B | 0 / 100 |
| Call of Pripyat | 3,816,080 B | 3,816,080 B | 0 B | 0 / 100 |
| Call of Pripyat EE | 3,818,312 B | 3,818,312 B | 0 B | 0 / 100 |
| SoC | 3,820,544 B | 3,820,544 B | 0 B | 0 / 100 |

No retained save buffers or repeatable managed-heap growth was observed. This is a focused Core test, not a process RSS or GUI leak measurement.

## NativeAOT Core and CLI publish (E4)

The CLI publish includes Core and the referenced Steam worker code in one self-contained Linux x64 NativeAOT executable. JSON serialization uses generated metadata; the publish below completed with `-warnaserror` and no trimming or AOT warnings.

```sh
HOME=<temporary-home> TMPDIR=<temporary-dir> NUGET_PACKAGES=<temporary-packages> \
  dotnet publish src/StalkerSaveEditor.Cli/StalkerSaveEditor.Cli.csproj \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishAot=true -p:PublishTrimmed=true -warnaserror -o <temporary-output>
```

Environment: Linux x64, .NET SDK 10.0.12, local x64 host. `StalkerSaveEditor.Cli version` was launched 11 times from the native publish; elapsed time was measured around each child process with a monotonic high-resolution clock.

| Artifact / measurement | Result |
| --- | ---: |
| Native CLI executable | 5,665,864 B |
| `libstalker_ooz.so` runtime dependency | 420,488 B |
| Combined runtime artifacts, excluding debug symbols | 6,086,352 B |
| CLI process start + `version`, median (11 runs) | 4.74 ms |
| Minimum / maximum | 4.30 ms / 14.29 ms |

This measures the Linux CLI process only. It does not measure Desktop startup, RSS, or publish size on Windows and macOS.
