Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Net.Http

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Read-SseDoneChunk {
    param(
        [Parameter(Mandatory = $true)][System.Net.Http.HttpClient]$Client,
        [Parameter(Mandatory = $true)][System.Net.Http.HttpRequestMessage]$Request
    )

    $response = $Client.SendAsync($Request, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
    if (-not $response.IsSuccessStatusCode) {
        throw "SSE request failed with HTTP $($response.StatusCode)."
    }

    $stream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
    $reader = New-Object System.IO.StreamReader($stream)
    $tokenCount = 0
    $tokenText = ""
    $doneChunk = $null

    while (-not $reader.EndOfStream) {
        $line = $reader.ReadLine()
        if ([string]::IsNullOrWhiteSpace($line) -or -not $line.StartsWith("data:")) {
            continue
        }

        $payload = $line.Substring(5).Trim()
        if ([string]::IsNullOrWhiteSpace($payload)) {
            continue
        }

        $chunk = $payload | ConvertFrom-Json
        if ($chunk.type -eq "token" -and $chunk.content) {
            $tokenCount += 1
            $tokenText += [string]$chunk.content
        }

        if ($chunk.type -eq "done") {
            $doneChunk = $chunk
            break
        }
    }

    Assert-Condition ($null -ne $doneChunk) "SSE stream completed without done chunk."
    return [pscustomobject]@{
        TokenCount = $tokenCount
        TokenText = $tokenText
        Done = $doneChunk
    }
}

function New-SessionHeaders {
    param(
        [Parameter(Mandatory = $true)][string]$ApiBase,
        [string]$Surface = "conversation",
        [string[]]$RequestedScopes = @(),
        [int]$TimeoutSec = 30
    )

    $body = @{
        surface = $Surface
    }

    if ($RequestedScopes.Count -gt 0) {
        $body.requestedScopes = $RequestedScopes
    }

    $session = Invoke-RestMethod `
        -Method Post `
        -Uri "$ApiBase/api/auth/session" `
        -ContentType "application/json" `
        -Body ($body | ConvertTo-Json -Depth 4) `
        -TimeoutSec $TimeoutSec

    Assert-Condition (-not [string]::IsNullOrWhiteSpace($session.accessToken)) "Session bootstrap returned empty token."

    return @{
        Authorization = "Bearer $($session.accessToken)"
    }
}
