param(
    [Parameter(Mandatory = $true)][string]$PlanPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-OptionalPropertyValue {
    param(
        [AllowNull()]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = @($Object.PSObject.Properties | Where-Object { $_.Name -eq $Name }) | Select-Object -First 1
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Set-StartInfoEnvironmentValue {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.ProcessStartInfo]$StartInfo,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $environmentProperty = @($StartInfo.PSObject.Properties | Where-Object { $_.Name -eq "Environment" }) | Select-Object -First 1
    if ($null -ne $environmentProperty) {
        $environmentProperty.Value[$Name] = $Value
        return
    }

    $environmentVariablesProperty = @($StartInfo.PSObject.Properties | Where-Object { $_.Name -eq "EnvironmentVariables" }) | Select-Object -First 1
    if ($null -ne $environmentVariablesProperty) {
        $environmentVariablesProperty.Value[$Name] = $Value
        return
    }

    throw "ProcessStartInfo does not expose an environment collection on this PowerShell host."
}

function Get-ReinvokeArgumentList {
    $items = New-Object System.Collections.Generic.List[string]
    foreach ($entry in $PSBoundParameters.GetEnumerator()) {
        $items.Add("-" + [string]$entry.Key)
        $items.Add([string]$entry.Value)
    }

    foreach ($argument in $MyInvocation.UnboundArguments) {
        $items.Add([string]$argument)
    }

    return @($items.ToArray())
}

if ($PSVersionTable.PSEdition -eq "Core") {
    $windowsPowerShell = Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe"
    if (Test-Path $windowsPowerShell) {
        & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath @(Get-ReinvokeArgumentList)
        exit $LASTEXITCODE
    }
}

function Get-TextTail {
    param(
        [AllowNull()][AllowEmptyCollection()][string[]]$Lines = @(),
        [int]$Count = 8
    )

    if ($null -eq $Lines -or $Lines.Count -eq 0) {
        return "<empty>"
    }

    return ($Lines | Select-Object -Last $Count) -join " || "
}

$plan = Get-Content -Path $PlanPath -Raw -Encoding UTF8 | ConvertFrom-Json

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = [string]$plan.FileName
$startInfo.Arguments = [string]$plan.Arguments
$startInfo.WorkingDirectory = [string]$plan.WorkingDirectory
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.CreateNoWindow = $true

$planEnvironment = Get-OptionalPropertyValue -Object $plan -Name "Environment"
if ($null -ne $planEnvironment) {
    foreach ($property in $planEnvironment.PSObject.Properties) {
        $name = [string]$property.Name
        $value = [string]$property.Value
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            Set-StartInfoEnvironmentValue -StartInfo $startInfo -Name $name -Value $value
        }
    }
}

$stdoutLines = New-Object System.Collections.Generic.List[string]
$stderrLines = New-Object System.Collections.Generic.List[string]
$stdoutHandler = [System.Diagnostics.DataReceivedEventHandler]{
    param($sender, $eventArgs)
    if ($null -ne $eventArgs.Data) {
        $stdoutLines.Add([string]$eventArgs.Data)
    }
}
$stderrHandler = [System.Diagnostics.DataReceivedEventHandler]{
    param($sender, $eventArgs)
    if ($null -ne $eventArgs.Data) {
        $stderrLines.Add([string]$eventArgs.Data)
    }
}

$process = New-Object System.Diagnostics.Process
$process.StartInfo = $startInfo
$process.EnableRaisingEvents = $true
$process.add_OutputDataReceived($stdoutHandler)
$process.add_ErrorDataReceived($stderrHandler)

$null = $process.Start()
$process.BeginOutputReadLine()
$process.BeginErrorReadLine()

$timeoutSec = [int]$plan.TimeoutSec
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$timedOut = $false

try {
    for ($elapsed = 5; $elapsed -le $timeoutSec; $elapsed += 5) {
        if (-not $process.WaitForExit(5000)) {
            $process.Refresh()
        }

        if ($process.HasExited) {
            break
        }
    }

    if (-not $process.HasExited) {
        $timedOut = $true
        try {
            $process.Kill($true)
            $process.WaitForExit()
        }
        catch {
        }
    }
}
finally {
    $stopwatch.Stop()
    try {
        $process.remove_OutputDataReceived($stdoutHandler)
        $process.remove_ErrorDataReceived($stderrHandler)
    }
    catch {
    }
}

$outputExists = Test-Path ([string]$plan.OutputPath)
$outputBytes = if ($outputExists) { (Get-Item ([string]$plan.OutputPath)).Length } else { 0 }
$exitCode = if ($timedOut) { $null } else { $process.ExitCode }
$success = (-not $timedOut) -and ($exitCode -eq 0) -and $outputExists -and ($outputBytes -gt 0)
$message = if ($success) {
    "Completed successfully."
}
elseif ($timedOut) {
    "Conversion timed out after $timeoutSec" + "s. stdout=" + (Get-TextTail -Lines @($stdoutLines.ToArray())) + " stderr=" + (Get-TextTail -Lines @($stderrLines.ToArray()))
}
else {
    "ExitCode=$exitCode outputExists=$outputExists outputBytes=$outputBytes stdout=" + (Get-TextTail -Lines @($stdoutLines.ToArray())) + " stderr=" + (Get-TextTail -Lines @($stderrLines.ToArray()))
}

$result = [pscustomobject]@{
    Success = $success
    TimedOut = $timedOut
    OutputExists = $outputExists
    OutputBytes = $outputBytes
    ExitCode = $exitCode
    Message = $message
    DurationMs = [int]$stopwatch.ElapsedMilliseconds
    StdoutTail = Get-TextTail -Lines @($stdoutLines.ToArray())
    StderrTail = Get-TextTail -Lines @($stderrLines.ToArray())
}

$result | ConvertTo-Json -Compress
exit $(if ($success) { 0 } else { 1 })
