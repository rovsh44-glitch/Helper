$ErrorActionPreference = "Stop"
Write-Host "[ToolBenchmark] Running tool-call correctness benchmark..."
dotnet test Helper.sln --filter "Category=ToolBenchmark" --no-build
Write-Host "[ToolBenchmark] Passed."

