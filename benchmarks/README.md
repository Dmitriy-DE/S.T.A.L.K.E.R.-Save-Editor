# Save format performance baselines

`SaveFormatBenchmarks` uses synthetic fixtures from `tests/Fixtures` and
BenchmarkDotNet's short run on the Ubuntu CI runner. The report includes
elapsed time and allocated bytes for LZO/Kraken decompression, the original
trilogy, Enhanced Edition releases, and S.T.A.L.K.E.R. 2.

CI runs the short benchmark job three independent times on `ubuntu-24.04`.
The time gate compares the median of those runs with the baseline only when the
CPU model and .NET JIT target match the time baseline (`AMD EPYC 7763`,
`x86-64-v3`). GitHub currently assigns multiple CPU families to this runner
label; on a different CPU/JIT target, the report records that timing as
non-comparable and keeps CI green. Allocation checks remain per-run on every
runner, so a single allocation regression still fails the job. Both gates use
the existing 30% benchmark regression limit. All run reports are uploaded as
the `performance-report` workflow artifact.

Time baselines remain from the original benchmark run. Allocation baselines
were refreshed from main workflow run `36313548439` at commit `ca52791` after
the X-Ray parser began retaining `XRayRegistryObject` metadata for stash
transfers in #39. The six trilogy and Enhanced Edition parse cases each gained
384 allocated bytes in the main report; decompression and S.T.A.L.K.E.R. 2
allocation values did not change. This records the measured main allocation
footprint without moving any time baseline.

To collect one benchmark sample locally, run the same command and update the
metadata and per-method values in `baseline.json` only after reviewing repeated
CI measurements and explaining any allocation changes:

```sh
dotnet run --project benchmarks/StalkerSaveEditor.Benchmarks.csproj \
  --configuration Release -- --filter '*' --job Short --exporters csv \
  --artifacts artifacts/benchmarks
```
