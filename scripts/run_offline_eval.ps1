$ErrorActionPreference = "Stop"
Write-Host "[OfflineEval] Running extended offline benchmark..."
dotnet test Helper.sln --filter "Category=EvalOffline" --no-build
Write-Host "[OfflineEval] Passed."

