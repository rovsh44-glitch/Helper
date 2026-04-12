# HELPER Certification Compile Hang Remediation Closure

Date: `2026-04-12`
Source plan: `doc/analysis/HELPER_CERTIFICATION_COMPILE_HANG_REMEDIATION_PLAN_2026-04-12.md`

## Closure Status

Status: `completed`

The remediation plan is implemented end-to-end.

The original reported `hang` condition is now treated as two separate contracts:

1. operational certification-compile execution
2. forensic blame-hang investigation

The default lane no longer depends on an unrealistically low blame timeout, the wrapper now owns teardown and idle detection, build target selection is deterministic, and template promotion no longer performs duplicate heavy post-activation certification by default.

## Implemented Changes

### 1. Wrapper teardown and operational/forensic split

Updated scripts:

- `scripts/run_certification_compile_tests.ps1`
- `scripts/run_compile_path_tests.ps1`

Implemented:

- canonical operational mode without blame collector
- explicit forensic mode via `-EnableBlameHang -BlameHangTimeoutSec <N>`
- enforced forensic timeout floor of `120s`
- default forensic timeout set to `180s`
- lane lock retention and bounded wait
- wall-time and idle-time contracts
- process-tree snapshots before teardown
- wrapper-owned `teardown_summary.json`
- hard failure if root process survives teardown
- preserved failure diagnostics under the run root

### 2. Deterministic dotnet target resolution

Updated runtime code:

- `src/Helper.Runtime/Infrastructure/Dotnet/DotnetBuildTargetResolver.cs`
- `src/Helper.Runtime/Core/Contracts/OperationsContracts.cs`
- `src/Helper.Runtime/DotnetService.cs`
- `src/Helper.Runtime/LocalBuildExecutor.cs`
- `src/Helper.Runtime/Generation/CompileGateWorkspacePreparer.cs`
- `src/Helper.Runtime/Generation/GenerationCompileGate.cs`
- `src/Helper.Api/Hosting/EndpointRegistrationExtensions.Evolution.Generation.cs`

Implemented:

- no unrestricted recursive first-match in default `BuildAsync`
- explicit overloads for `allowRecursiveDiscovery` and `targetPath`
- ignored-path filtering for `bin`, `obj`, `.compile_gate`, `.git`, `.vs`, `node_modules`, `__pycache__`
- structured ambiguity and invalid-target errors
- explicit compile-gate project targeting
- explicit real-build targeting through `LocalBuildExecutor`
- compatibility opt-in recursive discovery on `/api/build`

### 3. Promotion flow refactor

Updated runtime code:

- `src/Helper.Runtime/Generation/GenerationGuardrailsContracts.cs`
- `src/Helper.Runtime/Generation/TemplatePromotionFeatureProfileService.cs`
- `src/Helper.Runtime/Generation/TemplatePostActivationVerifier.cs`
- `src/Helper.Runtime/Generation/GenerationTemplatePromotionService.cs`
- `src/Helper.Runtime.Cli/HelperCliCommandDispatcher.Templates.cs`
- `src/Helper.Api/Hosting/EndpointRegistrationExtensions.Evolution.Generation.cs`

Implemented:

- new feature flag `HELPER_TEMPLATE_PROMOTION_POST_ACTIVATION_FULL_RECERTIFY`
- default `false`
- candidate-tree integrity snapshot after successful certification
- lightweight post-activation verification:
  - published root exists
  - candidate tree digest matches published tree
  - `certification_status.json` exists and stays passed
  - certification report path exists
  - lifecycle marks the promoted version active
- optional old behavior still available behind explicit full re-certification flag

### 4. Test and documentation updates

Updated tests:

- `test/Helper.Runtime.Tests/DotnetServiceTraceBehaviorTests.cs`
- `test/Helper.Runtime.Tests/TemplatePromotionPipelineTests.cs`
- `test/Helper.Runtime.Tests/TemplatePromotionEndToEndAndChaosTests.cs`
- `test/Helper.Runtime.CompilePath.Tests/TemplatePromotionCompileSmokeTests.cs`
- `test/Helper.Runtime.Tests/ArchitectureFitnessTests.RuntimeLanes.cs`
- `test/Helper.Runtime.Tests/ArchitectureFitnessTests.KnowledgeAndGeneration.cs`

Updated docs:

- `doc/operator/HELPER_RUNTIME_TEST_LANES_2026-04-01.md`
- `doc/operator/README.md`
- `doc/certification/reference/certification_protocol_golden_template.md`
- `doc/certification/reference/runbook_golden_promotion.md`

Documented:

- operational vs forensic certification-compile usage
- wrapper-owned diagnostics and teardown summary
- explicit blame timeout guidance
- default post-activation verification contract
- optional full re-certification mode

## Verification Evidence

### Build

- `dotnet build Helper.sln -m:1`
- `dotnet build test/Helper.Runtime.CompilePath.Tests/Helper.Runtime.CompilePath.Tests.csproj -m:1`

### Focused runtime regression tests

- `dotnet test test/Helper.Runtime.Tests/Helper.Runtime.Tests.csproj -c Debug -m:1 --no-build --filter "FullyQualifiedName~DotnetServiceTraceBehaviorTests|FullyQualifiedName~TemplatePromotionPipelineTests|FullyQualifiedName~ArchitectureFitnessTests.Runtime_Test_Lanes_Have_Dedicated_Projects_And_Entry_Points|FullyQualifiedName~ArchitectureFitnessTests.Certification_Compile_Dotnet_Tracing_And_Timeout_Policy_Are_Explicit|FullyQualifiedName~ArchitectureFitnessTests.RuntimeAndGenerationCoordinators_StayBehindBoundedCollaborators"`
  - result: `10/10`

### Compile-path regression tests

- `dotnet test test/Helper.Runtime.CompilePath.Tests/Helper.Runtime.CompilePath.Tests.csproj -c Debug -m:1 --no-build --filter "FullyQualifiedName~TemplatePromotionCompileSmokeTests|FullyQualifiedName~TemplateCertificationCompileSmokeTests"`
  - result: `2/2`

### Wrapper happy-path checks

- `powershell -ExecutionPolicy Bypass -File scripts/run_compile_path_tests.ps1 -Configuration Debug -NoBuild -NoRestore -Filter "FullyQualifiedName~TemplatePromotionCompileSmokeTests.PromotionPipeline_E2E_RouteToActivation_PassesForConsoleSmoke"`
  - result: `pass`

- `powershell -ExecutionPolicy Bypass -File scripts/run_certification_compile_tests.ps1 -Configuration Debug -NoBuild -NoRestore -Filter "FullyQualifiedName~TemplatePromotionEndToEndAndChaosTests.PromotionPipeline_E2E_RouteToActivation_PassesForConsoleSmoke"`
  - result: `pass`

- `powershell -ExecutionPolicy Bypass -File scripts/run_certification_compile_tests.ps1 -Configuration Debug -NoBuild -NoRestore -EnableBlameHang -BlameHangTimeoutSec 180 -Filter "FullyQualifiedName~TemplatePromotionEndToEndAndChaosTests.PromotionPipeline_E2E_RouteToActivation_PassesForConsoleSmoke"`
  - result: `pass`

### Controlled failure / teardown proof

- `powershell -ExecutionPolicy Bypass -File scripts/run_certification_compile_tests.ps1 -Configuration Missing -NoBuild -NoRestore -Filter "FullyQualifiedName~TemplatePromotionEndToEndAndChaosTests.PromotionPipeline_E2E_RouteToActivation_PassesForConsoleSmoke"`
  - result: wrapper failed as expected
  - preserved run root:
    - `C:\Users\rovsh\.codex\memories\msbuild-intermediate\repo\bin\test\Helper.Runtime.Certification.Compile.Tests\Missing\runs\20260412T075413190Z_3cbdc9d9`
  - teardown evidence:
    - `teardown_summary.json` reports `rootExited=true`
    - root process `19152` no longer existed after wrapper exit

### Lock-wait regression

- `powershell -ExecutionPolicy Bypass -File scripts/check_certification_compile_lock_wait.ps1 -Configuration Debug`
  - result: `pass`
  - distinct run ids observed for first and second invocations

## Outcome

The previous certification-compile `hang` diagnosis is closed.

What remains true:

- a deliberately too-small blame timeout can still kill real work
- heavy nested build tests are still heavy

What no longer remains true:

- the default certification-compile lane does not depend on blame mode
- template promotion does not duplicate full heavy certification after activation by default
- `DotnetService` no longer guesses build targets by unrestricted recursive first-match
- the wrapper no longer silently tolerates surviving root residue after failure teardown
