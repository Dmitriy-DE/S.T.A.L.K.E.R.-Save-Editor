# Save format performance baselines

`SaveFormatBenchmarks` uses synthetic fixtures from `tests/Fixtures` and
BenchmarkDotNet's short run on the Ubuntu CI runner. The report includes
elapsed time and allocated bytes for LZO/Kraken decompression, the original
trilogy, Enhanced Edition releases, and S.T.A.L.K.E.R. 2.

`baseline.json` records the first CI run on `ubuntu-24.04`. CI fails when a
method's mean time or allocated bytes exceeds its baseline by more than 30%.
Reports are uploaded as the `performance-report` workflow artifact.

To refresh the reference intentionally, run the same command and update the
metadata and per-method values in `baseline.json` from its CSV report:

```sh
dotnet run --project benchmarks/StalkerSaveEditor.Benchmarks.csproj \
  --configuration Release -- --filter '*' --job Short --exporters csv \
  --artifacts artifacts/benchmarks
```
