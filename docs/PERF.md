# Performance and memory observations

Measurements are observations from the stated local environment, not CI thresholds. Synthetic fixtures are used; these results do not describe the memory footprint of the packaged desktop UI or prove in-game behavior.

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
