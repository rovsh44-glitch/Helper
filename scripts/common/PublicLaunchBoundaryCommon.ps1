Set-StrictMode -Version Latest

function Get-PublicShowcaseBoundaryPolicy {
    return [PSCustomObject]@{
        policyVersion = "2026-03-24"
        publicationModel = "private_core_plus_public_showcase"
        publicSafeExact = @(
            "README.md",
            "CONTACT.md",
            "SECURITY.md",
            "CONTRIBUTING.md",
            "FAQ.md"
        )
        publicSafePrefixes = @(
            ".github/ISSUE_TEMPLATE/",
            "docs/",
            "media/"
        )
        privateOnlyPrefixes = @(
            "src/",
            "components/",
            "hooks/",
            "services/",
            "contexts/",
            "utils/",
            "modules/",
            "scripts/",
            "test/",
            "eval/",
            "mcp_config/",
            "searxng/",
            "doc/",
            "artifacts/",
            "temp/",
            "sandbox/",
            "concepts/",
            "node_modules/",
            "dist/",
            "tools/"
        )
        privateOnlyPatterns = @(
            ".env*",
            ".gitignore",
            ".ignore",
            "App.tsx",
            "Directory.Build.*",
            "Helper.sln",
            "docker-compose.yml",
            "eval_live_monitor.json",
            "index.*",
            "metadata.json",
            "package*.json",
            "personality.json",
            "postcss.config.cjs",
            "tailwind.config.cjs",
            "tsconfig.json",
            "types.ts",
            "vite-env.d.ts",
            "vite.config.ts",
            "vite.shared.config.mjs",
            "*.ps1",
            "*.bat"
        )
    }
}
function Get-RepoRelativeTopLevel {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    $normalized = $RelativePath -replace '\\', '/'
    return ($normalized -split '/')[0]
}

function Test-AnyWildcardMatch {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,
        [Parameter(Mandatory = $true)]
        [string[]]$Patterns
    )

    foreach ($pattern in $Patterns) {
        if ($Value -like $pattern) {
            return $pattern
        }
    }

    return $null
}

function Get-PublicShowcasePathClassification {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    $policy = Get-PublicShowcaseBoundaryPolicy
    $normalized = $RelativePath -replace '\\', '/'

    foreach ($exact in $policy.publicSafeExact) {
        if ($normalized.Equals($exact, [System.StringComparison]::OrdinalIgnoreCase)) {
            return [PSCustomObject]@{
                category = "public_safe"
                reason = "allowlist_exact"
                rule = $exact
            }
        }
    }

    foreach ($prefix in $policy.publicSafePrefixes) {
        if ($normalized.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return [PSCustomObject]@{
                category = "public_safe"
                reason = "allowlist_prefix"
                rule = $prefix
            }
        }
    }

    foreach ($prefix in $policy.privateOnlyPrefixes) {
        if ($normalized.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return [PSCustomObject]@{
                category = "private_only"
                reason = "denylist_prefix"
                rule = $prefix
            }
        }
    }

    $matchedPattern = Test-AnyWildcardMatch -Value $normalized -Patterns $policy.privateOnlyPatterns
    if ($null -ne $matchedPattern) {
        return [PSCustomObject]@{
            category = "private_only"
            reason = "denylist_pattern"
            rule = $matchedPattern
        }
    }

    return [PSCustomObject]@{
        category = "review_required"
        reason = "default_deny"
        rule = "default_deny"
    }
}
