# HELPER Runtime Service And Tooling Policies

Status: `active`
Updated: `2026-04-12`

## Solution Membership Policy

1. Every repository `*.csproj` under `src/` or `test/` must be classified explicitly.
2. Classification has only two valid states:
   - included in `Helper.sln`
   - listed in `scripts/config/solution-project-exclusions.json` with a reason
3. Unclassified projects are a gate failure.
4. `scripts/check_solution_build_coverage.ps1` is the canonical enforcement point for this policy.

## Runtime Service Profiles

Default profile: `production`

Prototype opt-in flag:

- `HELPER_ENABLE_PROTOTYPE_RUNTIME_SERVICES=true`

Rules:

1. Production profile is the default for API and CLI boot.
2. Production profile must not use fake-success executors.
3. `ICodeExecutor` resolves to `DisabledCodeExecutor` in production.
4. Prototype-only runtime services must require explicit opt-in via the environment flag above.
5. `PythonSandbox` is not treated as a production-ready execution path.

## Local Tool Execution Policy

1. Generic `shell_execute` is retired from the default built-in tool surface.
2. Structured tools such as `dotnet_test`, `read_file`, and `write_file` are the supported local tool entry points.
3. Interpreter commands such as `pwsh`, `powershell`, `python`, `node`, and `cmd` are blocked by `ProcessGuard`.
4. New local tools should expose narrow structured arguments instead of raw shell strings.
