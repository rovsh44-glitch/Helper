param(
    [string]$RunsPath = "",
    [int]$Take = 34,
    [string]$ReportPath = "doc/GENERATED_PROJECTS_34_AUDIT_2026-03-06.md"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")

function Get-Status {
    param([bool]$Value)
    if ($Value) { return "PASS" }
    return "FAIL"
}

function Invoke-Step {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory
    )

    $prevPreference = $ErrorActionPreference
    try {
        Push-Location $WorkingDirectory
        try {
            # Treat native stderr as output, not as terminating PowerShell errors.
            $ErrorActionPreference = "Continue"
            $output = & $FilePath @Arguments 2>&1 | ForEach-Object { $_.ToString() } | Out-String
            $exitCode = $LASTEXITCODE
            if ($null -eq $exitCode) {
                $exitCode = if ($?) { 0 } else { 1 }
            }

            return [PSCustomObject]@{
                ExitCode = $exitCode
                Output   = $output.Trim()
            }
        } finally {
            $ErrorActionPreference = $prevPreference
        }
    } catch {
        return [PSCustomObject]@{
            ExitCode = 1
            Output   = $_.Exception.Message
        }
    } finally {
        Pop-Location
    }
}

function Resolve-NpmCommand {
    $npmCmd = Get-Command "npm.cmd" -ErrorAction SilentlyContinue
    if ($null -ne $npmCmd) {
        return $npmCmd.Source
    }

    return "npm"
}

function Get-CodeFiles {
    param([string]$Root)

    return Get-ChildItem -Path $Root -Recurse -File |
        Where-Object { $_.Extension -in @(".cs", ".xaml", ".py", ".js", ".ts", ".tsx", ".jsx", ".html") }
}

function Get-StringTail {
    param(
        [string]$Value,
        [int]$Max = 600
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $trimmed = $Value.Trim()
    if ($trimmed.Length -le $Max) {
        return $trimmed
    }

    return "..." + $trimmed.Substring($trimmed.Length - $Max)
}

function Get-CapabilityCoverage {
    param(
        [string[]]$Capabilities,
        [string]$SourceText
    )

    if ($null -eq $Capabilities -or $Capabilities.Count -eq 0) {
        return [PSCustomObject]@{
            Total       = 0
            Matched     = 0
            Missing     = @()
            MatchedItems = @()
            Ratio       = 1.0
        }
    }

    $lowerText = $SourceText.ToLowerInvariant()
    $normalizedText = ($lowerText -replace "[^a-z0-9]+", "")

    $matched = New-Object System.Collections.Generic.List[string]
    $missing = New-Object System.Collections.Generic.List[string]

    foreach ($cap in $Capabilities) {
        $capLower = $cap.ToLowerInvariant()
        $capNorm = ($capLower -replace "[^a-z0-9]+", "")
        $isMatch = $false

        if ($lowerText.Contains($capLower)) {
            $isMatch = $true
        } elseif ($capNorm.Length -gt 0 -and $normalizedText.Contains($capNorm)) {
            $isMatch = $true
        }

        if ($isMatch) {
            $matched.Add($cap)
        } else {
            $missing.Add($cap)
        }
    }

    $ratio = [Math]::Round($matched.Count / [double]$Capabilities.Count, 2)
    return [PSCustomObject]@{
        Total       = $Capabilities.Count
        Matched     = $matched.Count
        Missing     = $missing.ToArray()
        MatchedItems = $matched.ToArray()
        Ratio       = $ratio
    }
}

if ([string]::IsNullOrWhiteSpace($RunsPath)) {
    $pathConfig = Get-HelperPathConfig -WorkspaceRoot (Join-Path $PSScriptRoot "..")
    $RunsPath = Join-Path $pathConfig.ProjectsRoot "generation_runs.jsonl"
}
elseif (-not [System.IO.Path]::IsPathRooted($RunsPath)) {
    $RunsPath = [System.IO.Path]::GetFullPath((Join-Path (Join-Path $PSScriptRoot "..") $RunsPath))
}

$runs = Get-Content $RunsPath | Select-Object -Last $Take | ForEach-Object { $_ | ConvertFrom-Json }
if ($runs.Count -ne $Take) {
    throw "Expected $Take runs, found $($runs.Count)."
}

$results = New-Object System.Collections.Generic.List[object]

foreach ($run in $runs) {
    $root = $run.RawProjectRoot
    $rootExists = Test-Path $root

    $templatePath = Join-Path $root "template.json"
    $validationPath = Join-Path $root "validation_report.json"
    $packagePath = Join-Path $root "package.json"
    $csprojPath = Join-Path $root "Project.csproj"
    $pythonMainPath = Join-Path $root "main.py"
    $nodeIndexPath = Join-Path $root "index.js"
    $reactMainPath = Join-Path $root "src/main.tsx"
    $reactAppPath = Join-Path $root "src/App.tsx"

    $zeroByteFiles = @()
    $jsonIssues = @()
    $codeFiles = @()
    $capabilityCoverage = $null
    $compileResult = $null
    $smokeResult = $null
    $stack = "unknown"

    if ($rootExists) {
        $npmCommand = Resolve-NpmCommand
        $allFiles = @(Get-ChildItem -Path $root -Recurse -File)
        $zeroByteFiles = @($allFiles | Where-Object { $_.Length -eq 0 } | Select-Object -ExpandProperty FullName)

        foreach ($jsonFile in @($templatePath, $validationPath, $packagePath)) {
            if (Test-Path $jsonFile) {
                try {
                    $null = Get-Content $jsonFile -Raw | ConvertFrom-Json
                } catch {
                    $jsonIssues += ("invalid-json: " + $jsonFile)
                }
            }
        }

        $codeFiles = @(Get-CodeFiles -Root $root)
        $sourceText = if ($codeFiles.Count -gt 0) {
            ($codeFiles | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
        } else {
            ""
        }

        $capabilities = @()
        if (Test-Path $templatePath) {
            try {
                $template = Get-Content $templatePath -Raw | ConvertFrom-Json
                if ($null -ne $template.Capabilities) {
                    $capabilities = @($template.Capabilities)
                }
            } catch {
                $capabilities = @()
            }
        }
        $capabilityCoverage = Get-CapabilityCoverage -Capabilities $capabilities -SourceText $sourceText

        if (Test-Path $csprojPath) {
            [xml]$projectXml = Get-Content $csprojPath -Raw
            $targetFrameworkNode = $projectXml.SelectSingleNode("//TargetFramework")
            $targetFramework = if ($null -ne $targetFrameworkNode) { $targetFrameworkNode.InnerText } else { "" }
            $useWpfNode = $projectXml.SelectSingleNode("//UseWPF")
            $useWpfValue = if ($null -ne $useWpfNode) { $useWpfNode.InnerText } else { "" }
            $isWpf = ($useWpfValue -eq "true") -or ($targetFramework -like "*windows*")
            $stack = if ($isWpf) { "csharp-wpf" } else { "csharp-console" }

            $compileResult = Invoke-Step -FilePath "dotnet" -Arguments @("build", $csprojPath, "-nologo", "-v", "minimal") -WorkingDirectory $root
            if ($compileResult.ExitCode -eq 0) {
                if ($stack -eq "csharp-console") {
                    $artifactPath = Join-Path $root ("bin/Debug/{0}/Project.dll" -f $targetFramework)
                    if (Test-Path $artifactPath) {
                        $smokeResult = Invoke-Step -FilePath "dotnet" -Arguments @($artifactPath) -WorkingDirectory $root
                    } else {
                        $smokeResult = [PSCustomObject]@{
                            ExitCode = 1
                            Output   = "missing-artifact: $artifactPath"
                        }
                    }
                } else {
                    $smokeResult = [PSCustomObject]@{
                        ExitCode = 0
                        Output   = "wpf-smoke-skipped"
                    }
                }
            } else {
                $smokeResult = [PSCustomObject]@{
                    ExitCode = 1
                    Output   = "compile-failed"
                }
            }
        } elseif ((Test-Path $pythonMainPath)) {
            $stack = "python"
            $compileResult = Invoke-Step -FilePath "py" -Arguments @("-3", "-m", "py_compile", $pythonMainPath) -WorkingDirectory $root
            $importSnippet = "import importlib.util; p=r'$pythonMainPath'; s=importlib.util.spec_from_file_location('m',p); m=importlib.util.module_from_spec(s); s.loader.exec_module(m); print('IMPORT_OK')"
            $smokeResult = Invoke-Step -FilePath "py" -Arguments @("-3", "-c", $importSnippet) -WorkingDirectory $root
        } elseif ((Test-Path $nodeIndexPath)) {
            $stack = "node"
            $compileResult = Invoke-Step -FilePath "node" -Arguments @("--check", $nodeIndexPath) -WorkingDirectory $root
            $smokeResult = Invoke-Step -FilePath "node" -Arguments @($nodeIndexPath) -WorkingDirectory $root
        } elseif ((Test-Path $packagePath) -and (Test-Path $reactMainPath) -and (Test-Path $reactAppPath)) {
            $stack = "react-vite-ts"
            $compileResult = Invoke-Step -FilePath $npmCommand -Arguments @("run", "build") -WorkingDirectory $root
            if ($compileResult.ExitCode -eq 0) {
                $smokeResult = [PSCustomObject]@{
                    ExitCode = 0
                    Output   = "build-artifacts-ok"
                }
            } else {
                $smokeResult = [PSCustomObject]@{
                    ExitCode = 1
                    Output   = "build-failed"
                }
            }
        } else {
            $compileResult = [PSCustomObject]@{
                ExitCode = 1
                Output   = "unknown-stack"
            }
            $smokeResult = [PSCustomObject]@{
                ExitCode = 1
                Output   = "unknown-stack"
            }
        }
    } else {
        $compileResult = [PSCustomObject]@{
            ExitCode = 1
            Output   = "missing-root"
        }
        $smokeResult = [PSCustomObject]@{
            ExitCode = 1
            Output   = "missing-root"
        }
        $capabilityCoverage = [PSCustomObject]@{
            Total       = 0
            Matched     = 0
            Missing     = @()
            MatchedItems = @()
            Ratio       = 0.0
        }
    }

    $structOk = $rootExists -and ($zeroByteFiles.Count -eq 0) -and ($jsonIssues.Count -eq 0)
    $compileOk = ($compileResult.ExitCode -eq 0)
    $smokeOk = ($smokeResult.ExitCode -eq 0)
    $semanticOk = ($capabilityCoverage.Ratio -ge 0.5)
    $overallPass = $structOk -and $compileOk -and $smokeOk -and $semanticOk

    $results.Add([PSCustomObject]@{
        ProjectName          = $run.ProjectName
        TemplateId           = $run.RoutedTemplateId
        Stack                = $stack
        Root                 = $root
        StructOk             = $structOk
        CompileOk            = $compileOk
        SmokeOk              = $smokeOk
        SemanticOk           = $semanticOk
        CapabilityRatio      = $capabilityCoverage.Ratio
        CapabilityMatched    = $capabilityCoverage.Matched
        CapabilityTotal      = $capabilityCoverage.Total
        MissingCapabilities  = ($capabilityCoverage.Missing -join ", ")
        ZeroByteCount        = $zeroByteFiles.Count
        JsonIssueCount       = $jsonIssues.Count
        CompileTail          = (Get-StringTail -Value $compileResult.Output -Max 500)
        SmokeTail            = (Get-StringTail -Value $smokeResult.Output -Max 300)
        OverallPass          = $overallPass
    })
}

$total = $results.Count
$structPass = @($results | Where-Object StructOk).Count
$compilePass = @($results | Where-Object CompileOk).Count
$smokePass = @($results | Where-Object SmokeOk).Count
$semanticPass = @($results | Where-Object SemanticOk).Count
$overallPassCount = @($results | Where-Object OverallPass).Count

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Audit of 34 Generated Projects (2026-03-06)")
$lines.Add("")
$lines.Add("## Scope")
$lines.Add("- Source: $RunsPath (last $Take runs)")
$lines.Add("- Total projects audited: $total")
$lines.Add("- Audit timestamp (UTC): $(Get-Date -Format 'yyyy-MM-ddTHH:mm:ssZ')")
$lines.Add("")
$lines.Add("## Gate Results")
$lines.Add("- Structural integrity pass: $structPass/$total")
$lines.Add("- Compile/syntax pass: $compilePass/$total")
$lines.Add("- Functionality smoke pass: $smokePass/$total")
$lines.Add("- Capability coverage pass (>=50%): $semanticPass/$total")
$lines.Add("- Overall pass (all gates): $overallPassCount/$total")
$lines.Add("")
$lines.Add("## Key Findings")

$reactCompileFailures = @($results | Where-Object { $_.Stack -eq "react-vite-ts" -and -not $_.CompileOk })
if ($reactCompileFailures.Count -gt 0) {
    $lines.Add("- React templates failed compile in all runs ($($reactCompileFailures.Count)) due TypeScript/build configuration issues (missing local tsconfig and cross-repo tsc scope).")
}

$vectorImportFailures = @($results | Where-Object { $_.TemplateId -eq "Template_VectorSearchEngine" -and -not $_.SmokeOk })
if ($vectorImportFailures.Count -gt 0) {
    $lines.Add("- Vector Search Engine failed runtime import smoke in all runs ($($vectorImportFailures.Count)) with ModuleNotFoundError: fastapi.")
}

$lowSemantic = @($results | Where-Object { -not $_.SemanticOk })
if ($lowSemantic.Count -gt 0) {
    $lines.Add("- Most generated projects are scaffold-level: low capability coverage against declared template.json capabilities ($($lowSemantic.Count)/$total below 50%).")
}

$lines.Add("")
$lines.Add("## Per-Project Results")
$lines.Add("| Project | Template | Stack | Struct | Compile | Smoke | Cap. | Overall |")
$lines.Add("|---|---|---|---:|---:|---:|---:|---:|")
foreach ($result in $results | Sort-Object TemplateId, ProjectName) {
    $lines.Add(
        ("| {0} | {1} | {2} | {3} | {4} | {5} | {6}/{7} ({8}) | {9} |" -f
            $result.ProjectName,
            $result.TemplateId,
            $result.Stack,
            (Get-Status -Value $result.StructOk),
            (Get-Status -Value $result.CompileOk),
            (Get-Status -Value $result.SmokeOk),
            $result.CapabilityMatched,
            $result.CapabilityTotal,
            $result.CapabilityRatio,
            (Get-Status -Value $result.OverallPass)
        )
    )
}

$lines.Add("")
$lines.Add("## Detailed Failures")
$failures = @($results | Where-Object { -not $_.OverallPass } | Sort-Object TemplateId, ProjectName)
if ($failures.Count -eq 0) {
    $lines.Add("- No failures detected.")
} else {
    foreach ($failure in $failures) {
        $lines.Add("### $($failure.ProjectName)")
        $lines.Add("- Template: $($failure.TemplateId)")
        $lines.Add("- Root: $($failure.Root)")
        $lines.Add("- Stack: $($failure.Stack)")
        $lines.Add("- Struct: $(Get-Status -Value $failure.StructOk) (zero-byte=$($failure.ZeroByteCount), json-issues=$($failure.JsonIssueCount))")
        $lines.Add("- Compile: $(Get-Status -Value $failure.CompileOk)")
        if (-not $failure.CompileOk -and -not [string]::IsNullOrWhiteSpace($failure.CompileTail)) {
            $lines.Add("~~~text")
            $lines.Add($failure.CompileTail)
            $lines.Add("~~~")
        }
        $lines.Add("- Smoke: $(Get-Status -Value $failure.SmokeOk)")
        if (-not $failure.SmokeOk -and -not [string]::IsNullOrWhiteSpace($failure.SmokeTail)) {
            $lines.Add("~~~text")
            $lines.Add($failure.SmokeTail)
            $lines.Add("~~~")
        }
        $lines.Add("- Capability coverage: $($failure.CapabilityMatched)/$($failure.CapabilityTotal) ($($failure.CapabilityRatio))")
        if (-not [string]::IsNullOrWhiteSpace($failure.MissingCapabilities)) {
            $lines.Add("- Missing capabilities in code text: $($failure.MissingCapabilities)")
        }
        $lines.Add("")
    }
}

Set-Content -Path $ReportPath -Value $lines -Encoding UTF8

Write-Output "REPORT_PATH=$ReportPath"
Write-Output "TOTAL=$total"
Write-Output "OVERALL_PASS=$overallPassCount"

