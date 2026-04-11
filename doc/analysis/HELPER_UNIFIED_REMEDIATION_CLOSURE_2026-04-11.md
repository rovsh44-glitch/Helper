# HELPER Unified Remediation Closure

Date: `2026-04-11`

Source plan: `doc/analysis/HELPER_UNIFIED_REMEDIATION_PLAN_2026-04-11.md`

## Closure summary

The local remediation scope defined by the unified plan is implemented.

Completed:

1. heavy-report default outputs on the active heavy path were rerouted away from `doc/`
2. current generated residue was preserved under `temp/verification/preserved_doc_residue/2026-04-11/`
3. `doc/` was cleaned of blocked machine-local tokens
4. deterministic `scripts/ci_gate.ps1` now passes end-to-end
5. separate `scripts/ci_gate_heavy.ps1` now writes fresh heavy artifacts under `temp/verification/heavy/` instead of polluting `doc/`

## Implemented changes

### Heavy output rerouting

The following scripts now default to `temp/verification` locations instead of `doc/` top-level residue:

1. `scripts/load_streaming_chaos.ps1`
2. `scripts/run_generation_parity_gate.ps1`
3. `scripts/run_generation_parity_benchmark.ps1`
4. `scripts/run_generation_parity_window_gate.ps1`
5. `scripts/run_generation_parity_nightly.ps1`

### Guard coverage

`test/Helper.Runtime.Tests/ArchitectureFitnessTests.RuntimeLanes.cs` now includes a regression guard ensuring the active heavy report scripts do not default back to `doc/` residue.

### Evidence preservation

Previous broken-state artifacts were preserved here:

1. `temp/verification/preserved_doc_residue/2026-04-11/HELPER_PARITY_GATE_2026-04-11_11-13-32.json`
2. `temp/verification/preserved_doc_residue/2026-04-11/HELPER_PARITY_GATE_2026-04-11_11-13-32.md`
3. `temp/verification/preserved_doc_residue/2026-04-11/load_streaming_chaos_report.md`

## Verification

Passed locally:

1. `dotnet build Helper.sln -m:1`
2. `powershell -ExecutionPolicy Bypass -File scripts/run_load_chaos_smoke.ps1 -Configuration Debug -NoBuild -NoRestore`
3. `powershell -ExecutionPolicy Bypass -File scripts/run_eval_gate.ps1 -NoBuild -NoRestore`
4. `powershell -ExecutionPolicy Bypass -File scripts/run_tool_benchmark.ps1 -Configuration Debug -NoBuild -NoRestore`
5. `dotnet test test/Helper.Runtime.Tests/Helper.Runtime.Tests.csproj -c Debug -m:1 --no-build --no-restore --filter "FullyQualifiedName~ArchitectureFitnessTests.Heavy_Report_Scripts_Do_Not_Default_To_Doc_Root_Residue" -v minimal`
6. `dotnet test test/Helper.Runtime.Tests/Helper.Runtime.Tests.csproj -c Debug -m:1 --no-build --no-restore --filter "FullyQualifiedName~ArchitectureFitnessTests.PublicWorkingTree_Files_And_Contents_Are_Free_Of_Disallowed_Brand_Tokens" -v minimal`
7. `powershell -ExecutionPolicy Bypass -File scripts/ci_gate.ps1`

Post-heavy verification:

1. `powershell -ExecutionPolicy Bypass -File scripts/ci_gate_heavy.ps1` now writes to `temp/verification/heavy/`
2. `doc/` remains free of blocked machine-local brand tokens after the heavy run

## Current heavy-gate status

`scripts/ci_gate_heavy.ps1` no longer fails because of orchestration drift or `doc/` residue.

It now fails at the intended product/data gate:

1. `Generation parity gate`
2. violation: `insufficient_golden_sample`
3. violation: `GenerationSuccessRate 0.00% < 95.00%`

Fresh heavy artifacts were written here:

1. `temp/verification/heavy/load_streaming_chaos_report.md`
2. `temp/verification/heavy/HELPER_PARITY_GATE_2026-04-11_18-53-05.md`
3. `temp/verification/heavy/HELPER_PARITY_GATE_2026-04-11_18-53-05.json`

This is an honest KPI/data blocker, not the earlier remediation defect.

## Boundary

Not closed by local implementation alone:

1. push to remote `main`
2. hosted GitHub status attachment on real commits
3. branch protection enforcement

Those steps require an explicit remote update workflow and were not executed as part of this local remediation pass.
